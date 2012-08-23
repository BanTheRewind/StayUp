/*
* 
* Copyright (c) 2012, Ban the Rewind
* All rights reserved.
* 
* Redistribution and use in source and binary forms, with or 
* without modification, are permitted provided that the following 
* conditions are met:
* 
* Redistributions of source code must retain the above copyright 
* notice, this list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright 
* notice, this list of conditions and the following disclaimer in 
* the documentation and/or other materials provided with the 
* distribution.
* 
* Neither the name of the Ban the Rewind nor the names of its 
* contributors may be used to endorse or promote products 
* derived from this software without specific prior written 
* permission.
* 
* THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS 
* "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT 
* LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS 
* FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE 
* COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, 
* INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
* BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; 
* LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER 
* CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, 
* STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
* ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF 
* ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
* 
*/

// Imports
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace StayUp
{

	/// <summary>
	/// Main application
	/// </summary>
	class Program
	{
		#region Constants
		/// <summary>
		/// Application log title
		/// </summary>
		private const string	kLogTitle				= "Stay Up";

		/// <summary>
		/// Console line separator
		/// </summary>
		private const string	kSeparator				= "\n========================================";
		#endregion
		#region Static members
		/// <summary>
		/// Process name (eg, "MyApp.exe")
		/// </summary>
		private static string   sApplication;

		/// <summary>
		/// Event log
		/// </summary>
		private static EventLog	sEventLog;

		/// <summary>
		/// Event logging flag
		/// </summary>
		private static bool	sEventLogEnabled;

		/// <summary>
		/// Information log timer interval
		/// </summary>
		private static TimeSpan sInfoInterval;

		/// <summary>
		/// Information log timer 
		/// </summary>
		private static Timer	sInfoTimer;

		/// <summary>
		/// Time elapsed since target process became non-reponsive
		/// </summary>
		private static TimeSpan sNotRespondingTime;

		/// <summary>
		/// Unresponsive duration before the target process is forced closed
		/// </summary>
		private static TimeSpan	sNotRespondingTimeout;

		/// <summary>
		/// Peak paged memory size of target process
		/// </summary>
		private static long     sPeakPagedMemorySize;

		/// <summary>
		/// Peak virtual memory size of target process
		/// </summary>
		private static long     sPeakVirtualMemorySize;

		/// <summary>
		/// The target process
		/// </summary>
		private static Process  sProcess;
		
		/// <summary>
		/// Target process ID
		/// </summary>
		private static int		sProcessId;

		/// <summary>
		/// Target process name
		/// </summary>
		private static string	sProcessName;

		/// <summary>
		/// Process duration
		/// </summary>
		private static TimeSpan sTotalProcessorTime;

		/// <summary>
		/// Duration of main application loop
		/// </summary>
		private static TimeSpan	sUpdateInterval;

		/// <summary>
		/// Main application timer
		/// </summary>
		private static Timer	sUpdateTimer;

		/// <summary>
		/// Flags whether help message has been displayed
		/// </summary>
		private static bool		sWroteHelpMessage;
		#endregion
		#region Methods
		/// <summary>
		/// Builds and return general info string
		/// </summary>
		/// <returns></returns>
		static string GetInfoString()
		{
			return ">> Process name: " + sProcessName + "\n" +
				">> Process ID: " + sProcessId + "\n" +
				">> Duration: " + sTotalProcessorTime.ToString() + "\n" + 
				">> Machine name: " + Environment.MachineName + "\n" + 
				">> Peak memory (paged/virtual): " + sPeakPagedMemorySize + "/" + sPeakVirtualMemorySize;
		}

		/// <summary>
		/// Launch the application
		/// </summary>
		private static bool Launch()
		{
			// Reset not responding clock
			sNotRespondingTime = TimeSpan.FromMilliseconds( 0 );

			// Get application path
			string appPath = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location ) + "\\" + sApplication;

			// Set application location
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			Log( "Process starting at \"" + appPath + "\"." );
			processStartInfo.FileName = appPath;

			// Configure process
			sProcess = new Process();
			sProcess.StartInfo = processStartInfo;
			sProcess.EnableRaisingEvents = true;
			sProcess.ErrorDataReceived += new DataReceivedEventHandler( ErrorHandler );
			sProcess.Exited += new EventHandler( ExitHandler );

			// Run application
			try {
				sProcess.Start();
			} catch ( Exception ex ) {
				Log( "Unable to start process:\n" + ex.Message + "\n" + ex.InnerException );
				return false;
			}

			// Get process name and ID
			sProcessId = sProcess.Id;
			sProcessName = sProcess.ProcessName;
			Log( "Process started:\n>> Process name: " + sProcessName );

			// Start timers
			StartTimers();

			// Clean up
			processStartInfo = null;

			return true;
		}

		/// <summary>
		/// Writes message to log and debug console
		/// </summary>
		/// <param name="message">The message</param>
		public static void Log( string message )
		{
			Log( message, EventLogEntryType.Information );
		}

		/// <summary>
		/// Writes message to log and debug console
		/// </summary>
		/// <param name="message">The message</param>
		/// <param name="type">Log entry type</param>
		public static void Log( string message, EventLogEntryType type )
		{
			if ( sEventLogEnabled ) {
				sEventLog.WriteEntry( message, type );
			}
			Debug.WriteLine( message + kSeparator );
			Console.WriteLine( message + kSeparator );
		}

		/// <summary>
		/// Application entry
		/// </summary>
		/// <param name="args">Command line arguments</param>
		public static void Main( string[] args )
		{
			// Create event log if it does not exist
			if ( !System.Diagnostics.EventLog.Exists( kLogTitle ) ) {
				System.Diagnostics.EventLog.LogNameFromSourceName( kLogTitle, Environment.MachineName );
			}
			if ( !System.Diagnostics.EventLog.SourceExists( kLogTitle ) ) {
				System.Diagnostics.EventLog.CreateEventSource( kLogTitle, kLogTitle );
			}
			sEventLog = new EventLog( kLogTitle, Environment.MachineName, kLogTitle );

			// Define properties
			sApplication = "sample.exe";
			sEventLogEnabled = true;
			sInfoInterval = TimeSpan.FromSeconds( 3600.0 );
			sNotRespondingTimeout = TimeSpan.FromSeconds( 15.0 );
			sPeakPagedMemorySize = 0L;
			sPeakVirtualMemorySize = 0L;
			sProcessId = -1;
			sProcessName = "";
			sUpdateInterval = TimeSpan.FromMilliseconds( 33.0 ); // 60fps
			sWroteHelpMessage = false;

			// Read application name
			if ( sApplication.Length <= 0 ) {
				sApplication = args.Length > 0 ? args[ 0 ] : "";
				if ( sApplication.Length <= 0 ) {
					WriteHelp();
					return;
				}
			}

			// Read arguments
			if ( args.Length > 1 ) {
				for ( int i = 1; i < args.Length; i += 2 ) {
					if ( i + 1 < args.Length ) {
						if ( args[ i ] == "-e" ) {
							int logEnabled = 1;
							if ( int.TryParse( args[ i + 1 ], out logEnabled ) ) {
								sEventLogEnabled = logEnabled > 0;
							} else {
								WriteHelp();
							}
						} else if ( args[ i ] == "-f" ) {
							double framerate = 60.0;
							if ( double.TryParse( args[ i + 1 ], out framerate ) ) {
								sUpdateInterval = TimeSpan.FromMilliseconds( 1.0 / framerate );
							} else {
								WriteHelp();
							}
						} else if ( args[ i ] == "-i" ) {
							double interval = 15.0;
							if ( double.TryParse( args[ i + 1 ], out interval ) ) {
								sInfoInterval = TimeSpan.FromSeconds( interval );
							} else {
								WriteHelp();
							}
						} else if ( args[ i ] == "-t" ) {
							double timeout = 15.0;
							if ( double.TryParse( args[ i + 1 ], out timeout ) ) {
								sNotRespondingTimeout = TimeSpan.FromSeconds( timeout );
							} else {
								WriteHelp();
							}
						} else {
							WriteHelp();
						}	
					}
				}
			}

			// Intro
			Console.WriteLine( "\nSTAY UP 1.1.0" + kSeparator );
			
			// Launch application
			if ( !Launch() ) {
				return;
			}
			Console.ReadLine();
			
		}

		/// <summary>
		/// Starts timers
		/// </summary>
		static void StartTimers()
		{
			double delay = 1.0;
			sInfoTimer = new Timer( new TimerCallback( InfoTimerHandler ), null, TimeSpan.FromSeconds( delay ), sInfoInterval );
			sUpdateTimer = new Timer( new TimerCallback( UpdateTimerHandler ), null, TimeSpan.FromSeconds( delay ), sUpdateInterval );
		}

		/// <summary>
		/// Stop timers
		/// </summary>
		static void StopTimers()
		{
			sInfoTimer.Dispose();
			sUpdateTimer.Dispose();
		}

		/// <summary>
		/// Writes help to console
		/// </summary>
		static void WriteHelp()
		{
			if ( !sWroteHelpMessage ) {
				sWroteHelpMessage = true;
				Console.WriteLine( "Usage:   StayUp [process] -e [0/1] -f [framerate] -i [interval] -t [timeout]" );
				Console.WriteLine( "         -e     Event log enabled or disabled. Default is 1 (true)." ); 
				Console.WriteLine( "         -f     Application frame rate. Default is 60." );
				Console.WriteLine( "         -i     Interval at which information is logged, " );
				Console.WriteLine( "                in seconds. Default is 3600." );
				Console.WriteLine( "         -t     Unresponsive time before forced restart, " );
				Console.WriteLine( "                in seconds. Default is 15." );
				Console.WriteLine( "" );
				Console.WriteLine( "Example: StayUp MyApp.exe -e 1 -f 60 -i 3600 -t 15" );
			}
		}
		#endregion
		#region Events
		/// <summary>
		/// Handles application error
		/// </summary>
		/// <param name="sender">Sender</param>
		/// <param name="e">Event arguments</param>
		private static void ErrorHandler( object sender, DataReceivedEventArgs e )
		{
			Log( "An error occurred\n" + e.Data, EventLogEntryType.Error );
		}

		/// <summary>
		/// Handles application exit
		/// </summary>
		/// <param name="sender">Sender</param>
		/// <param name="e">Event arguments</param>
		private static void ExitHandler( object sender, EventArgs e )
		{
			StopTimers();
			if ( sProcess == null ) {
				return;
			}
			int code = sProcess.ExitCode;
			if ( code == 1 ) {
				Log( "Process ended:\n" +
					">> Exit code:    " + sProcess.ExitCode + "\n" +
					">> Exit time:    " + DateTime.Now.ToString() + "\n" +
					GetInfoString() );
			} else {
				Log( "Process ended:\n" +
					">> Exit code:    " + sProcess.ExitCode + "\n" +
					">> Exit time:    " + DateTime.Now.ToString() + "\n" +
					GetInfoString(), EventLogEntryType.Error );
			}
			Launch();
		}

		/// <summary>
		/// Handles timer events
		/// </summary>
		/// <param name="state">Timer object</param>
		private static void InfoTimerHandler( object sender )
		{
			if ( sProcess == null || sProcess.HasExited ) {
				return;
			}
			Log( "Process information:\n" + GetInfoString() );
		}

		/// <summary>
		/// Main application callback
		/// </summary>
		/// <param name="state">Timer object</param>
		private static void UpdateTimerHandler( object sender )
		{
			if ( sProcess == null || sProcess.HasExited ) {
				return;
			}

			// Update statistics
			sPeakPagedMemorySize = sProcess.PeakPagedMemorySize64;
			sPeakVirtualMemorySize = sProcess.PeakVirtualMemorySize64;
			sTotalProcessorTime = sProcess.TotalProcessorTime;

			// Monitor application responsiveness, force close if frozen
			if ( !sProcess.Responding ) {
				sNotRespondingTime.Add( sUpdateInterval );
				if ( sNotRespondingTime > sNotRespondingTimeout ) {
					Log( sProcessName + " has stopped responding for at least " + sNotRespondingTimeout.ToString() + "." );
					sProcess.Close();
				}
			} else {
				sNotRespondingTime = TimeSpan.FromMilliseconds( 0.0 );
			}
		}
		#endregion
	}

}

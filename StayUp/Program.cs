/*
* 
* Copyright (c) 2013, Ban the Rewind
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

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
		const string	kLogTitle				= "Stay Up";

		/// <summary>
		/// Console line separator
		/// </summary>
		const string	kSeparator				= "\n========================================";

		/// <summary>
		/// Version number
		/// </summary>
		const string	kVersion				= "1.1.0.6";
		
		#endregion
		#region Static members

		/// <summary>
		/// Process name (eg, "MyApp.exe")
		/// </summary>
		private static string   sApplication			= "";

		/// <summary>
		/// Event log
		/// </summary>
		private static EventLog	sEventLog;

		/// <summary>
		/// Event logging flag
		/// </summary>
		private static bool		sEventLogEnabled		= false;

		/// <summary>
		/// Information log timer interval
		/// </summary>
		private static TimeSpan sInfoInterval			= TimeSpan.FromSeconds( 3600.0 );

		/// <summary>
		/// Information log timer 
		/// </summary>
		private static Timer	sInfoTimer;

		/// <summary>
		/// Flag set when process stops responding
		/// </summary>
		private static bool		sNotResponding			= false;

		/// <summary>
		/// Time, in milliseconds, to wait for an application before restarting it
		/// </summary>
		private static int		sNotRespondingTimeout	= 5000;

		/// <summary>
		/// Peak memory size of target process
		/// </summary>
		private static long     sPeakMemorySize			= 0L;

		/// <summary>
		/// The target process
		/// </summary>
		private static Process  sProcess;
		
		/// <summary>
		/// Target process ID
		/// </summary>
		private static int		sProcessId				= -1;

		/// <summary>
		/// Target process name
		/// </summary>
		private static string	sProcessName			= "";

		/// <summary>
		/// Process duration
		/// </summary>
		private static TimeSpan sTotalProcessorTime		= TimeSpan.FromMilliseconds( 0.0 );

		/// <summary>
		/// Main application timer
		/// </summary>
		private static Timer	sUpdateTimer;

		/// <summary>
		/// Flags whether help message has been displayed
		/// </summary>
		private static bool		sWroteHelpMessage		= false;
		
		#endregion
		#region External

		/// <summary>
		/// Sends timeout message to process
		/// </summary>
		/// <returns>Process handle</returns>
		[DllImport( "user32.dll", CharSet = CharSet.Auto )]
		private static extern IntPtr SendMessageTimeout(
			HandleRef hWnd,
			int msg,
			IntPtr wParam,
			IntPtr lParam,
			int flags,
			int timeout,
			out IntPtr pdwResult );

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
				">> Peak memory: " + sPeakMemorySize;
		}

		/// <summary>
		/// Checks if process is responding
		/// </summary>
		/// <returns>True if responding</returns>
		private static bool IsResponding()
		{
			if ( sProcess == null || sProcess.HasExited ) {
				return false;
			}
			HandleRef handleRef = new HandleRef( sProcess, sProcess.MainWindowHandle );
			IntPtr lpdwResult;
			IntPtr lResult = SendMessageTimeout(
				handleRef,
				0,
				IntPtr.Zero,
				IntPtr.Zero,
				2,
				sNotRespondingTimeout,
				out lpdwResult );
			return lResult != IntPtr.Zero;
		}

		/// <summary>
		/// Launch the application
		/// </summary>
		private static bool Launch()
		{
			// Reset not responding flag
			sNotResponding = false;

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
			try {
				if ( !System.Diagnostics.EventLog.Exists( kLogTitle ) ) {
					System.Diagnostics.EventLog.LogNameFromSourceName( kLogTitle, Environment.MachineName );
				}
				if ( !System.Diagnostics.EventLog.SourceExists( kLogTitle ) ) {
					try {
						System.Diagnostics.EventLog.CreateEventSource( kLogTitle, kLogTitle );
					} catch ( System.Security.SecurityException ex ) {
						Console.WriteLine( ex.Message + "\n" );
						return;
					}
				}
			} catch ( System.Security.SecurityException ex ) {
				Console.WriteLine( ex.Message + "\n" );
				return;
			}
			sEventLog = new EventLog( kLogTitle, Environment.MachineName, kLogTitle );

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
				for ( int i = 1; i < args.Length; ++i ) {
					if ( args[ i ] == "-e" ) {
						sEventLogEnabled = true;
					} else if ( args[ i ] == "-i" ) {
						++i;
						double interval = 15.0;
						if ( i < args.Length && double.TryParse( args[ i ], out interval ) ) {
							sInfoInterval = TimeSpan.FromSeconds( interval );
						} else {
							WriteHelp();
						}
					} else if ( args[ i ] == "-t" ) {
						++i;
						double timeout = 5.0;
						if ( i < args.Length && double.TryParse( args[ i ], out timeout ) ) {
							sNotRespondingTimeout = (int)( timeout * 1000.0 );
						} else {
							WriteHelp();
						}

					} else {
						WriteHelp();
					}	
				}
			}

			// Intro
			Console.WriteLine( "\nSTAY UP " + kVersion + kSeparator );
			
			// Launch application
			if ( !Launch() ) {
				Environment.Exit( 1 );
				return;
			}
			Console.ReadLine();
			
		}

		/// <summary>
		/// Starts timers
		/// </summary>
		static void StartTimers()
		{
			TimeSpan delay = TimeSpan.FromMilliseconds( (double)sNotRespondingTimeout );
			sInfoTimer = new Timer( new TimerCallback( InfoTimerHandler ), null, TimeSpan.FromSeconds( 1.0 ), sInfoInterval );
			sUpdateTimer = new Timer( new TimerCallback( UpdateTimerHandler ), null, delay, delay );
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
				Console.WriteLine( "Usage:   StayUp [process] -e -i [interval] -t [timeout]" );
				Console.WriteLine( "         -e     Enabled event log. Disabled by default." ); 
				Console.WriteLine( "         -i     Interval at which information is logged, " );
				Console.WriteLine( "                in seconds. Default is 3600." );
				Console.WriteLine( "         -t     Time to wait for a process to respond before forcing it" );
				Console.WriteLine( "                to restart, in seconds. Default is 5." );
				Console.WriteLine( "" );
				Console.WriteLine( "Example: StayUp MyApp.exe -e -i 3600 -t 5" );
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
			sPeakMemorySize = sProcess.WorkingSet64;
			sTotalProcessorTime = sProcess.TotalProcessorTime;

			// Monitor application responsiveness, force close if frozen
			if ( !IsResponding() ) {
				if ( !sNotResponding ) {
					sNotResponding = true;
					Log( "Process has stopped responding:\n>> Process name: " + sProcessName );
					try {
						sProcess.Kill();
					} catch ( Exception ex ) {
						Console.WriteLine( ex.Message + "\n" );
					}
				}
			} else {
				sNotResponding = false;
			}
		}

		#endregion
	}

}

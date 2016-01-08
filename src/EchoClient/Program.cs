﻿namespace EchoClient
{
	using System;
	using System.Net;
	using System.Net.Sockets;
	using System.Threading;
	using RabbitMqNext.Internals;

	class Program
	{
		private static AutoResetEvent _event = new AutoResetEvent(false);
		private static SocketRingBuffers _socket2Streams;
		private static CancellationTokenSource cancellationToken = new CancellationTokenSource();
		private static AmqpPrimitivesWriter _amqpWriter;
		private static AmqpPrimitivesReader _amqpReader;

		const string TargetHost = "media";
		const int port = 6767;

		static void Main(string[] args)
		{
			AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
			{
				Console.WriteLine("wtf? " + eventArgs.ExceptionObject.ToString());
			};
			AppDomain.CurrentDomain.FirstChanceException += (sender, eventArgs) =>
			{
				Console.WriteLine("ops " + eventArgs.Exception.ToString());
			};

//			Console.WriteLine("Is Server GC: " + GCSettings.IsServerGC);
//			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
//			Console.WriteLine("Compaction mode: " + GCSettings.LargeObjectHeapCompactionMode);
//			Console.WriteLine("Latency mode: " + GCSettings.LatencyMode);
//			GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
//			Console.WriteLine("New Latency mode: " + GCSettings.LatencyMode);

			var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
			socket.Connect(Dns.GetHostAddresses(TargetHost)[1], port);

			Console.CancelKeyPress += (sender, eventArgs) =>
			{
				Console.WriteLine("Cancelling...");
				cancellationToken.Cancel();
				socket.Close();
				_event.Set();
			};

			Console.WriteLine("Client started");

			_socket2Streams = new SocketRingBuffers(socket, cancellationToken.Token, delegate
			{
				cancellationToken.Cancel();
				Console.WriteLine("closed...");
			});
			_amqpWriter = new AmqpPrimitivesWriter(_socket2Streams.Writer, null, null);
			_amqpReader = new AmqpPrimitivesReader(_socket2Streams.Reader);

//			Task.Factory.StartNew(WriteFrames, cancellationToken, TaskCreationOptions.LongRunning);
//			Task.Factory.StartNew(ReadFrames, cancellationToken, TaskCreationOptions.LongRunning);
			new Thread(WriteFrames) { IsBackground = true, Name = "A"}.Start();
			new Thread(ReadFrames) { IsBackground = true, Name = "B" }.Start();

			_event.WaitOne();

			Console.WriteLine("Done..");
			// Thread.CurrentThread.Join();
		}

		const int TotalBytesToWrite = 10000;
		const int TotalShortsToWrite = 10000;
		const int TotalIntsToWrite = 10000;
		const int TotalStringsToWrite = 10000;
		const string Str =
			"Traces generated from a single logical operation can be tagged " +
			"with an operation-unique identity, in order to distinguish them " +
			"from traces from a different logical operation. For example, it " +
			"may be useful to group correlated traces by ASP.NET request. The " +
			"CorrelationManager class provides methods used to store a logical " +
			"operation identity in a thread-bound context and automatically tag " +
			"each trace event generated by the thread with the stored identity.";

		private static void WriteFrames(object obj)
		{
			try
			{
				ulong iteration = 0L;

				while (!cancellationToken.IsCancellationRequested)
				{
					Console.WriteLine("Wr Iteration started " + iteration);

					for (uint i = 0; i < TotalBytesToWrite; i++)
					{
						_socket2Streams.Writer.Write((byte)((byte)i % 256));
					}

					for (uint i = 0; i < TotalShortsToWrite; i++)
					{
						_socket2Streams.Writer.Write((ushort)(i % ushort.MaxValue));
					}

					for (uint i = 0; i < TotalIntsToWrite; i++)
					{
						_socket2Streams.Writer.Write((uint)(i % uint.MaxValue));
					}

					for (uint i = 0; i < TotalStringsToWrite; i++)
					{
						_amqpWriter.WriteLongstr(Str);
					}

					Console.WriteLine("Wr Iteration complete " + iteration++);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("WriteFrames " + ex);
				throw;
			}
		}

		private static async void ReadFrames(object obj)
		{
			try
			{
				ulong iteration = 0L;

				while (!cancellationToken.IsCancellationRequested)
				{
					Console.WriteLine("R Iteration started " + iteration);

					for (uint i = 0; i < TotalBytesToWrite; i++)
					{
						byte b = await _socket2Streams.Reader.ReadByte();
						var exp = (byte)i % 256;
						if (b != exp)
						{
							Console.WriteLine("[1] Issue found at " +
								_socket2Streams.Reader._ringBufferStream.Position +
								" Expecting " + exp + " but got " + b);
						}
					}
					for (uint i = 0; i < TotalShortsToWrite; i++)
					{
						ushort b = _socket2Streams.Reader.ReadUInt16();
						var exp = (ushort)i % ushort.MaxValue;
						if (b != exp)
						{
							Console.WriteLine("[2] Issue found at " +
								_socket2Streams.Reader._ringBufferStream.Position +
								" Expecting " + exp + " but got " + b);
						}
					}

					for (uint i = 0; i < TotalIntsToWrite; i++)
					{
						// _socket2Streams.Writer.Write((uint)i % uint.MaxValue);
						uint b = _socket2Streams.Reader.ReadUInt32();
						var exp = (uint)i % uint.MaxValue;
						if (b != exp)
						{
							Console.WriteLine("[3] Issue found at " +
								_socket2Streams.Reader._ringBufferStream.Position +
								" Expecting " + exp + " but got " + b);
						}
					}

					for (uint i = 0; i < TotalStringsToWrite; i++)
					{
						var b = await _amqpReader.ReadLongstr();
	
						if (b != Str)
						{
							Console.WriteLine("[3] Issue found at " +
								_socket2Streams.Reader._ringBufferStream.Position +
								" Expecting \n" + b + " but got \n" + Str);
						}
					}

					Console.WriteLine("R Iteration complete " + iteration++);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("ReadFrames " + ex);
				throw;
			}
		}
	}
}

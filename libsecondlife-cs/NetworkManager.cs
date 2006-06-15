/*
 * Copyright (c) 2006, Second Life Reverse Engineering Team
 * All rights reserved.
 *
 * - Redistribution and use in source and binary forms, with or without 
 *   modification, are permitted provided that the following conditions are met:
 *
 * - Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 * - Neither the name of the Second Life Reverse Engineering Team nor the names 
 *   of its contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" 
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF 
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
 * POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Text;
using System.Timers;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Security.Cryptography;
using Nwc.XmlRpc;

namespace libsecondlife
{
	public delegate void PacketCallback(Packet packet, Circuit circuit);

	internal class AcceptAllCertificatePolicy : ICertificatePolicy
	{
		public AcceptAllCertificatePolicy()
		{
		}

		public bool CheckValidationResult(ServicePoint sPoint, 
			System.Security.Cryptography.X509Certificates.X509Certificate cert, 
			WebRequest wRequest,int certProb)
		{
			// Always accept
			return true;
		}
	}

	public class Circuit
	{
		public uint CircuitCode;
		public bool Opened;
		public ushort Sequence;
		public IPEndPoint ipEndPoint;

		private EndPoint endPoint;
		private ProtocolManager Protocol;
		private NetworkManager Network;
		private byte[] Buffer;
		private Socket Connection;
		private AsyncCallback ReceivedData;
		private System.Timers.Timer OpenTimer;
		private System.Timers.Timer ACKTimer;
		private bool Timeout;
		private ArrayList AckOutbox;
		private Mutex AckOutboxMutex;
		private Hashtable NeedAck;
		private Mutex NeedAckMutex;
		private ArrayList Inbox;
		private Mutex InboxMutex;
		private int ResendTick;

		public Circuit(ProtocolManager protocol, NetworkManager network, uint circuitCode)
		{
			Initialize(protocol, network, circuitCode);
		}

		public Circuit(ProtocolManager protocol, NetworkManager network)
		{
			// Generate a random circuit code
			System.Random random = new System.Random();

			Initialize(protocol, network, (uint)random.Next());
		}

		private void Initialize(ProtocolManager protocol, NetworkManager network, uint circuitCode)
		{
			Protocol = protocol;
			Network = network;
			CircuitCode = circuitCode;
			Sequence = 0;
			Buffer = new byte[4096];
			Connection = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			Opened = false;
			Timeout = false;

			// Initialize the queue of ACKs that need to be sent to the server
			AckOutbox = new ArrayList();

			// Initialize the hashtable for reliable packets waiting on ACKs from the server
			NeedAck = new Hashtable();

			Inbox = new ArrayList();

			// Create a timer to test if the connection times out
			OpenTimer = new System.Timers.Timer(10000);
			OpenTimer.Elapsed += new ElapsedEventHandler(OpenTimerEvent);

			// Create a timer to send PacketAcks and resend unACKed packets
			ACKTimer = new System.Timers.Timer(1000);
			ACKTimer.Elapsed += new ElapsedEventHandler(ACKTimerEvent);

			AckOutboxMutex = new Mutex(false, "AckOutboxMutex");
			NeedAckMutex = new Mutex(false, "NeedAckMutex");
			InboxMutex = new Mutex(false, "InboxMutex");

			ResendTick = 0;
		}

		~Circuit()
		{
			Close();
		}

		public bool Open(string ip, int port)
		{
			return Open(IPAddress.Parse(ip), port);
		}

		public bool Open(IPAddress ip, int port)
		{
			try
			{
				// Setup the callback
				ReceivedData = new AsyncCallback(this.OnReceivedData);

				// Create an endpoint that we will be communicating with (need it in two types due to
				// .NET weirdness)
				ipEndPoint = new IPEndPoint(ip, port);
				endPoint = (EndPoint)ipEndPoint;

				// Associate this circuit's socket with the given ip and port and start listening
				Connection.Connect(endPoint);
				Connection.BeginReceiveFrom(Buffer, 0, Buffer.Length, SocketFlags.None, ref endPoint, ReceivedData, null);

				// Start the circuit opening timeout
				OpenTimer.Start();

				// Start the packet resend timer
				ACKTimer.Start();

				// Send the UseCircuitCode packet to initiate the connection
				Packet packet = PacketBuilder.UseCircuitCode(Protocol, Network.AgentID, 
					Network.SessionID, CircuitCode);

				// Send the initial packet out
				SendPacket(packet, true);

				while (!Timeout)
				{
					if (Opened)
					{
						return true;
					}

					Thread.Sleep(0);
				}
			}
			catch (Exception e)
			{
				Helpers.Log(e.ToString(), Helpers.LogLevel.Error);
			}

			return false;
		}

		public void Close()
		{
			try
			{
				Opened = false;

				StopTimers();

				// TODO: Is this safe? Using the mutex throws an exception about a disposed object
				NeedAck.Clear();

				//Connection.EndReceiveFrom(

				// Send the CloseCircuit notice
				Packet packet = new Packet("CloseCircuit", Protocol, 8);
				Connection.Send(packet.Data);

				// Shut the socket communication down
				Connection.Shutdown(SocketShutdown.Both);
			}
			catch (Exception e)
			{
				Helpers.Log(e.ToString(), Helpers.LogLevel.Error);
			}
		}

		public void StopTimers()
		{
			// Stop the resend timer
			ACKTimer.Stop();

			// Stop the open circuit timer (just in case it's still running)
			OpenTimer.Stop();
		}

		public void SendPacket(Packet packet, bool incrementSequence)
		{
			byte[] zeroBuffer = new byte[4096];
			int zeroBytes;

			if (!Opened && packet.Layout.Name != "UseCircuitCode")
			{
				Helpers.Log("Trying to send a " + packet.Layout.Name + " packet when the socket is closed",
					Helpers.LogLevel.Warning);
				return;
			}

			// DEBUG
			//Console.WriteLine("Sending " + packet.Data.Length + " byte " + packet.Layout.Name);

			try
			{
				if ((packet.Data[0] & Helpers.MSG_RELIABLE) != 0 && incrementSequence)
				{
					if (!NeedAck.ContainsKey(packet))
					{
						// This packet needs an ACK, keep track of when it was sent out
						NeedAckMutex.WaitOne();
						NeedAck.Add(packet, Environment.TickCount);
						NeedAckMutex.ReleaseMutex();
					}
				}

				if (incrementSequence)
				{
					// Set the sequence number here since we are manually serializing the packet
					packet.Sequence = ++Sequence;
				}

				// Zerocode if needed
				if ((packet.Data[0] & Helpers.MSG_ZEROCODED) != 0)
				{
					zeroBytes = Helpers.ZeroEncode(packet.Data, packet.Data.Length, zeroBuffer);
				}
				else
				{
					// Normal packet, copy it straight over to the zeroBuffer
					Array.Copy(packet.Data, 0, zeroBuffer, 0, packet.Data.Length);
					zeroBytes = packet.Data.Length;
				}

				// The incrementSequence check prevents a possible deadlock situation
				if (AckOutbox.Count != 0 && incrementSequence && packet.Layout.Name != "PacketAck" && 
					packet.Layout.Name != "LogoutRequest")
				{
					// Claim the mutex on the AckOutbox
					AckOutboxMutex.WaitOne();

					//TODO: Make sure we aren't appending more than 255 ACKs

					// Append each ACK needing to be sent out to this packet
					foreach (uint ack in AckOutbox)
					{
						Array.Copy(BitConverter.GetBytes(ack), 0, zeroBuffer, zeroBytes, 4);
						zeroBytes += 4;
					}

					// Last byte is the number of ACKs
					zeroBuffer[zeroBytes] = (byte)AckOutbox.Count;
					zeroBytes += 1;

					AckOutbox.Clear();

					// Release the mutex
					AckOutboxMutex.ReleaseMutex();

					// Set the flag that this packet has ACKs appended to it
					zeroBuffer[0] += Helpers.MSG_APPENDED_ACKS;
				}

				int numSent = Connection.Send(zeroBuffer, zeroBytes, SocketFlags.None);

				// DEBUG
				//Console.WriteLine("Sent " + numSent + " bytes");
			}
			catch (Exception e)
			{
				Helpers.Log(e.ToString(), Helpers.LogLevel.Error);
			}
		}

		private void SendACKs()
		{
			// Claim the mutex on the AckOutbox
			AckOutboxMutex.WaitOne();

			if (AckOutbox.Count != 0)
			{
				try
				{
					// TODO: Take in to account the 255 ACK limit per packet
					Packet packet = PacketBuilder.PacketAck(Protocol, AckOutbox);

					// Set the sequence number
					packet.Sequence = ++Sequence;

					// Bypass SendPacket since we are taking care of the AckOutbox ourself
					int numSent = Connection.Send(packet.Data);

					// DEBUG
					//Console.WriteLine("Sent " + numSent + " byte " + packet.Layout.Name);

					AckOutbox.Clear();
				}
				catch (Exception e)
				{
					Helpers.Log(e.ToString(), Helpers.LogLevel.Error);
				}
			}

			// Release the mutex
			AckOutboxMutex.ReleaseMutex();
		}

		private void OnReceivedData(IAsyncResult result)
		{
			Packet packet;

			try
			{
				// For the UseCircuitCode timeout
				Opened = true;
				OpenTimer.Stop();

				// Retrieve the incoming packet
				int numBytes = Connection.EndReceiveFrom(result, ref endPoint);

				if ((Buffer[Buffer.Length - 1] & Helpers.MSG_APPENDED_ACKS) != 0)
				{
					// Grab the ACKs that are appended to this packet
					byte numAcks = Buffer[Buffer.Length - 1];

					Helpers.Log("Found " + numAcks + " appended acks", Helpers.LogLevel.Info);

					// Claim the NeedAck mutex
					NeedAckMutex.WaitOne();

					for (int i = 1; i <= numAcks; ++i)
					{
						uint ack = BitConverter.ToUInt32(Buffer, numBytes - i * 4 - 1);

						ArrayList reliablePackets = (ArrayList)NeedAck.Keys;

						for (int j = reliablePackets.Count - 1; j >= 0; j--)
						{
							Packet reliablePacket = (Packet)reliablePackets[i];

							if ((uint)reliablePacket.Sequence == ack)
							{
								NeedAck.Remove(reliablePacket);
							}
						}
					}

					// Release the mutex
					NeedAckMutex.ReleaseMutex();

					// Adjust the packet length
					numBytes = numBytes - numAcks * 4 - 1;
				}

				if ((Buffer[0] & Helpers.MSG_ZEROCODED) != 0)
				{
					// Allocate a temporary buffer for the zerocoded packet
					byte[] zeroBuffer = new byte[4096];
					int zeroBytes = Helpers.ZeroDecode(Buffer, numBytes, zeroBuffer);
					packet = new Packet(zeroBuffer, zeroBytes, Protocol);
					numBytes = zeroBytes;
				}
				else
				{
					// Create the packet object from our byte array
					packet = new Packet(Buffer, numBytes, Protocol);
				}

				// DEBUG
				//Console.WriteLine("Received a " + numBytes + " byte " + packet.Layout.Name);

				// Start listening again since we're done with Buffer
				Connection.BeginReceiveFrom(Buffer, 0, Buffer.Length, SocketFlags.None, ref endPoint, ReceivedData, null);

				// Track the sequence number for this packet if it's marked as reliable
				if ((packet.Data[0] & Helpers.MSG_RELIABLE) != 0)
				{
					// Check if this is a duplicate packet
					InboxMutex.WaitOne();
					AckOutboxMutex.WaitOne();

					if (Inbox.Contains(packet.Sequence))
					{
						if (AckOutbox.Contains((uint)packet.Sequence))
						{
							Helpers.Log("ACKs are being sent too slowly!", Helpers.LogLevel.Warning);
						}
						else
						{
							// DEBUG
							//Helpers.Log("Received a duplicate " + packet.Layout.Name + " packet, sequence=" + 
							//	packet.Sequence + ", not in the ACK outbox", Helpers.LogLevel.Info);

							// Add this packet to the AckOutbox again and bypass the callbacks
							AckOutbox.Add((uint)packet.Sequence);
						}

						// Avoid firing a callback twice for the same packet
						Inbox.Add(packet.Sequence);
						AckOutboxMutex.ReleaseMutex();
						InboxMutex.ReleaseMutex();
						return;
					}

					// Add this packet to the incoming log
					Inbox.Add(packet.Sequence);

					if (!AckOutbox.Contains((uint)packet.Sequence))
					{
						AckOutbox.Add((uint)packet.Sequence);
					}
					else
					{
						if ((packet.Data[0] & Helpers.MSG_RESENT) != 0)
						{
							// We received a resent packet
							Helpers.Log("Received a resent packet, sequence=" + packet.Sequence, Helpers.LogLevel.Warning);
							return;
						}
						else
						{
							// We received a resent packet
							Helpers.Log("Received a duplicate sequence number? sequence=" + packet.Sequence
								+ ", name=" + packet.Layout.Name, Helpers.LogLevel.Warning);
						}
					}

					AckOutboxMutex.ReleaseMutex();
					InboxMutex.ReleaseMutex();
				}

				if (packet.Layout.Name == null)
				{
					Helpers.Log("Received an unrecognized packet", Helpers.LogLevel.Warning);
					return;
				}
				else if (packet.Layout.Name == "PacketAck")
				{
					// PacketAck is handled directly instead of using a callback to simplify access to 
					// the NeedAck hashtable and its mutex

					ArrayList blocks = packet.Blocks();

					NeedAckMutex.WaitOne();

					// Remove each ACK in this packet from the NeedAck waiting list
					foreach (Block block in blocks)
					{
						foreach (Field field in block.Fields)
						{
						Beginning:
							ICollection reliablePackets = NeedAck.Keys;

							// Remove this packet if it exists
							foreach (Packet reliablePacket in reliablePackets)
							{
								if ((uint)reliablePacket.Sequence == (uint)field.Data)
								{
									NeedAck.Remove(reliablePacket);
									// Restart the loop to avoid upsetting the enumerator
									goto Beginning;
								}
							}
						}
					}

					NeedAckMutex.ReleaseMutex();
				}
				
				// Fire any internal callbacks registered with this packet type
				PacketCallback callback = (PacketCallback)Network.InternalCallbacks[packet.Layout.Name];

				if (callback != null)
				{
					callback(packet, this);
				}

				// Fire any user callbacks registered with this packet type
				callback = (PacketCallback)Network.UserCallbacks[packet.Layout.Name];
				
				if (callback != null)
				{
					callback(packet, this);
				}
				else
				{
					// Attempt to fire a default user callback
					callback = (PacketCallback)Network.UserCallbacks["Default"];

					if (callback != null)
					{
						callback(packet, this);
					}
				}
			}
			catch (Exception e)
			{
				Helpers.Log(e.ToString(), Helpers.LogLevel.Error);
			}
		}

		private void OpenTimerEvent(object source, System.Timers.ElapsedEventArgs ea)
		{
			try
			{
				Timeout = true;
				OpenTimer.Stop();
			}
			catch (Exception e)
			{
				Helpers.Log(e.ToString(), Helpers.LogLevel.Error);
			}
		}

		private void ACKTimerEvent(object source, System.Timers.ElapsedEventArgs ea)
		{
			try
			{
				// Send any ACKs in the queue
				SendACKs();

				ResendTick++;

				if (ResendTick >= 3)
				{
					ResendTick = 0;

				Beginning:

					// Check if any reliable packets haven't been ACKed by the server
					NeedAckMutex.WaitOne();
					IDictionaryEnumerator packetEnum = NeedAck.GetEnumerator();
					NeedAckMutex.ReleaseMutex();

					while (packetEnum.MoveNext())
					{
						int ticks = (int)packetEnum.Value;

						// TODO: Is this hardcoded value correct? Should it be a higher level define or a 
						//       changeable property?
						if (Environment.TickCount - ticks > 3000)
						{
							Packet packet = (Packet)packetEnum.Key;

							// Adjust the timeout value for this packet
							NeedAckMutex.WaitOne();

							if (NeedAck.ContainsKey(packet))
							{
								NeedAck[packet] = Environment.TickCount;
								NeedAckMutex.ReleaseMutex();

								// Add the resent flag
								packet.Data[0] += Helpers.MSG_RESENT;
							
								// Resend the packet
								SendPacket((Packet)packet, false);

								Helpers.Log("Resending " + packet.Layout.Name + " packet, sequence=" + packet.Sequence, 
									Helpers.LogLevel.Info);

								// Rate limiting
								System.Threading.Thread.Sleep(100);

								// Restart the loop since we modified a value and the iterator will fail
								goto Beginning;
							}
							else
							{
								NeedAckMutex.ReleaseMutex();
							}
						}
					}
				}
			}
			catch (Exception e)
			{
				Helpers.Log(e.ToString(), Helpers.LogLevel.Error);
			}
		}
	}

	public class NetworkManager
	{
		public LLUUID AgentID;
		public LLUUID SessionID;
		public string LoginError;
		public Hashtable UserCallbacks;
		public Hashtable InternalCallbacks;
		public Circuit CurrentCircuit;

		private ProtocolManager Protocol;
		private ArrayList Circuits;

		public NetworkManager(ProtocolManager protocol)
		{
			Protocol = protocol;
			Circuits = new ArrayList();
			UserCallbacks = new Hashtable();
			InternalCallbacks = new Hashtable();
			CurrentCircuit = null;

			// Register the internal callbacks
			PacketCallback callback = new PacketCallback(RegionHandshakeHandler);
			InternalCallbacks["RegionHandshake"] = callback;
			callback = new PacketCallback(StartPingCheckHandler);
			InternalCallbacks["StartPingCheck"] = callback;
		}

		public void SendPacket(Packet packet)
		{
			if (CurrentCircuit != null)
			{
				CurrentCircuit.SendPacket(packet, true);
			}
			else
			{
				Helpers.Log("Trying to send a packet when there is no current circuit", Helpers.LogLevel.Error);
			}
		}

		public void SendPacket(Packet packet, Circuit circuit)
		{
			circuit.SendPacket(packet, true);
		}

		public static Hashtable DefaultLoginValues(string firstName, string lastName, string password, string mac,
			string startLocation, int major, int minor, int patch, int build, string platform, string viewerDigest, 
			string userAgent, string author)
		{
			Hashtable values = new Hashtable();

			// Generate an MD5 hash of the password
			MD5 md5 = new MD5CryptoServiceProvider();
			byte[] hash = md5.ComputeHash(Encoding.ASCII.GetBytes(password));
			StringBuilder passwordDigest = new StringBuilder();
			// Convert the hash to a hex string
			foreach(byte b in hash)
			{
				passwordDigest.AppendFormat("{0:x2}", b);
			}

			values["first"] = firstName;
			values["last"] = lastName;
			values["passwd"] = "$1$" + passwordDigest;
			values["start"] = startLocation;
			values["major"] = major;
			values["minor"] = minor;
			values["patch"] = patch;
			values["build"] = build;
			values["platform"] = platform;
			values["mac"] = mac;
			values["viewer_digest"] = viewerDigest;
			values["user-agent"] = userAgent + " (" + Helpers.VERSION + ")";
			values["author"] = author;

			return values;
		}

		public bool Login(Hashtable loginParams, out Hashtable values)
		{
			return Login(loginParams, "https://login.agni.lindenlab.com/cgi-bin/login.cgi", out values);
		}

		public bool Login(Hashtable loginParams, string url, out Hashtable values)
		{
			XmlRpcResponse result;
			XmlRpcRequest xmlrpc = new XmlRpcRequest();
			xmlrpc.MethodName = "login_to_simulator";
			xmlrpc.Params.Clear();
			xmlrpc.Params.Add(loginParams);

			try
			{
				result = (XmlRpcResponse)xmlrpc.Send(url);
			}
			catch (Exception e)
			{
				Helpers.Log(e.ToString(), Helpers.LogLevel.Error);
				LoginError = e.Message;
				values = null;
				return false;
			}

			if (result.IsFault)
			{
				Helpers.Log("Fault " + result.FaultCode + ": " + result.FaultString, Helpers.LogLevel.Error);
				LoginError = "Fault " + result.FaultCode + ": " + result.FaultString;
				values = null;
				return false;
			}

			values = (Hashtable)result.Value;

			if ((string)values["login"] == "false")
			{
				LoginError = values["reason"] + ": " + values["message"];
				return false;
			}

			AgentID = new LLUUID((string)values["agent_id"]);
			SessionID = new LLUUID((string)values["session_id"]);
			uint circuitCode = (uint)(int)values["circuit_code"];

			// Connect to the sim given in the login reply
			Circuit circuit = new Circuit(Protocol, this, circuitCode);
			if (!circuit.Open((string)values["sim_ip"], (int)values["sim_port"]))
			{
				return false;
			}

			// Circuit was successfully opened, add it to the list and set it as default
			Circuits.Add(circuit);
			CurrentCircuit = circuit;

			// Move our agent in to the sim to complete the connection
			Packet packet = PacketBuilder.CompleteAgentMovement(Protocol, AgentID, SessionID, circuitCode);
			SendPacket(packet);

			return true;
		}

		public bool Connect(IPAddress ip, ushort port, bool setDefault)
		{
			Circuit circuit = new Circuit(Protocol, this);
			if (!circuit.Open(ip, port))
			{
				return false;
			}

			Circuits.Add(circuit);

			if (setDefault)
			{
				CurrentCircuit = circuit;
			}

			return true;
		}

		public void Disconnect(uint circuitCode)
		{
			foreach (Circuit circuit in Circuits)
			{
				if (circuit.CircuitCode == circuitCode)
				{
					circuit.Close();
					Circuits.Remove(circuit);
					return;
				}
			}
		}

		public void Logout()
		{
			// TODO: Close all circuits except the current one

			// Halt all timers on the current circuit
			CurrentCircuit.StopTimers();

			Packet packet = PacketBuilder.LogoutRequest(Protocol, AgentID, SessionID);
			SendPacket(packet);

			// TODO: We should probably check if the server actually received the logout request
			// Instead we'll use this silly Sleep()
			System.Threading.Thread.Sleep(1000);
		}

		private void StartPingCheckHandler(Packet packet, Circuit circuit)
		{
			//TODO: Should we care about OldestUnacked?

			// Respond to the ping request
			Packet pingPacket = PacketBuilder.CompletePingCheck(Protocol, packet.Data[5]);
			SendPacket(pingPacket, circuit);
		}

		private void RegionHandshakeHandler(Packet packet, Circuit circuit)
		{
			// Send a RegionHandshakeReply
			Packet replyPacket = new Packet("RegionHandshakeReply", Protocol, 12);
			SendPacket(replyPacket, circuit);
		}
	}
}

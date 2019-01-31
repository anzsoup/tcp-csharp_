using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace ChickenIngot.Networking
{
	public enum TokenState
	{
		// 대기중
		Idle,

		// 연결됨
		Connected,

		// 종료가 예약됨.
		// sending_list에 대기중인 상태에서 disconnect를 호출한 경우,
		// 남아있는 패킷을 모두 보낸 뒤 끊도록 하기 위한 상태값.
		ReserveClosing,

		// 소켓 종료 상태
		Closed,
	}

	public class UserToken
	{
		// 종료 요청. S -> C
		private const short SYS_CLOSE_REQ = -1;
		// 종료 응답. C -> S
		private const short SYS_CLOSE_ACK = -2;
		// 하트비트 시작. S -> C
		public const short SYS_START_HEARTBEAT = -3;
		// 하트비트 갱신. C -> S
		public const short SYS_UPDATE_HEARTBEAT = -4;

		// close중복 처리 방지를 위한 플래그.
		// 0 = 연결된 상태.
		// 1 = 종료된 상태.
		private int isClosed;
		

		// 바이트를 패킷 형식으로 해석해주는 해석기
		private readonly MessageResolver _messageResolver;
		// Session 객체. 어플리케이션 딴에서 구현하여 사용.
		private IPeer _peer;
		// BufferList적용을 위해 queue에서 list로 변경.
		private List<ArraySegment<byte>> _sendingList;
		// _sendingQueue lock처리에 사용되는 객체
		private object _csSendingQueue;

		private IMessageDispatcher _dispatcher;

		public delegate void ClosedDelegate(UserToken token);
		public ClosedDelegate onSessionClosed;

		// heartbeat.
		public long LatestHeartbeatTime { get; private set; }
		private HeartbeatSender _heartbeatSender;
		private bool _autoHeartbeat;

		public SocketAsyncEventArgs ReceiveEventArgs { get; private set; }
		public SocketAsyncEventArgs SendEventArgs { get; private set; }

		public Socket Socket { get; private set; }

		public string IP { get; private set; }
		public int Port { get; private set; }
		public long RoundTripTime { get; private set; }
		public PingReply PingReply { get; private set; }
		
		public TokenState State { get; private set; }

		public int SendingListCount { get { return _sendingList.Count; } }

		public UserToken(IMessageDispatcher dispatcher)
		{
			_dispatcher = dispatcher;
			_csSendingQueue = new object();

			_messageResolver = new MessageResolver();
			_peer = null;
			_sendingList = new List<ArraySegment<byte>>();
			LatestHeartbeatTime = DateTime.Now.Ticks;

			State = TokenState.Idle;
		}

		public void OnConnected()
		{
			State = TokenState.Connected;
			isClosed = 0;
			_autoHeartbeat = true;
		}

		public void SetPeer(IPeer peer)
		{
			_peer = peer;
		}

		public void SetSocket(Socket socket)
		{
			Socket = socket;

			IPEndPoint endPoint = socket.RemoteEndPoint as IPEndPoint;
			IP = endPoint.Address.ToString();
			Port = endPoint.Port;
		}

		public void SetEventArgs(SocketAsyncEventArgs receiveArgs, SocketAsyncEventArgs sendArgs)
		{
			ReceiveEventArgs = receiveArgs;
			SendEventArgs = sendArgs;
		}

		/// <summary>
		/// 이 메소드에서 직접 바이트 데이터를 해석해도 되지만 Message resolver클래스를 따로 둔 이유는
		/// 추후에 확장성을 고려하여 다른 resolver를 구현할 때 UserToken 클래스의 코드 수정을 최소화하기 위함이다.
		/// </summary>
		public void OnReceive(byte[] buffer, int offset, int transferred)
		{
			_messageResolver.OnReceive(buffer, offset, transferred, OnMessageCompleted);
		}

		private void OnMessageCompleted(ArraySegment<byte> buffer)
		{
			if (_peer == null) return;

			if (_dispatcher == null)
			{
				// IO스레드에서 직접 호출.
				Packet msg = new Packet(buffer, this);
				OnMessage(msg);
			}
			else
			{
				// 로직 스레드의 큐를 타고 호출되도록 함.
				_dispatcher.OnMessage(this, buffer);
			}
		}

		public void OnMessage(Packet msg)
		{
			// active close를 위한 코딩.
			//   서버에서 종료하라고 연락이 왔는지 체크한다.
			//   만약 종료신호가 맞다면 disconnect를 호출하여 받은쪽에서 먼저 종료 요청을 보낸다.
			switch (msg.ProtocolID)
			{
				case SYS_CLOSE_REQ:
					Disconnect();
					return;

				case SYS_START_HEARTBEAT:
					{
						// 순서대로 파싱해야 하므로 프로토콜 아이디는 버린다.
						msg.PopProtocolID();
						// 전송 인터벌.
						byte interval = msg.PopByte();
						_heartbeatSender = new HeartbeatSender(this, interval);

						if (_autoHeartbeat)
						{
							StartHeartbeat();
						}
					}
					return;

				case SYS_UPDATE_HEARTBEAT:
					LatestHeartbeatTime = DateTime.Now.Ticks;
					return;
			}


			if (_peer != null)
			{
				try
				{
					switch (msg.ProtocolID)
					{
						case SYS_CLOSE_ACK:
							_peer.OnRemoved();
							break;

						default:
							_peer.OnMessage(msg);
							break;
					}
				}
				catch (Exception)
				{
					Close();
				}
			}

			if (msg.ProtocolID == SYS_CLOSE_ACK)
			{
				if (onSessionClosed != null)
				{
					onSessionClosed(this);
				}
			}
		}

		public void Close()
		{
			// 중복 수행을 막는다.
			if (Interlocked.CompareExchange(ref isClosed, 1, 0) == 1)
			{
				return;
			}

			if (State == TokenState.Closed)
			{
				// already closed.
				return;
			}

			State = TokenState.Closed;
			if (Socket != null)
			{
				Socket.Close();
				Socket = null;
			}

			SendEventArgs.UserToken = null;
			ReceiveEventArgs.UserToken = null;

			_sendingList.Clear();
			_messageResolver.ClearBuffer();

			if (_peer != null)
			{
				Packet msg = Packet.Create(SYS_CLOSE_ACK);
				if (_dispatcher == null)
				{
					OnMessage(msg);
				}
				else
				{
					_dispatcher.OnMessage(this, new ArraySegment<byte>(msg.Buffer, 0, msg.Position));
				}
			}
		}

		/// <summary>
		/// 패킷을 전송한다.
		/// 큐가 비어있을 경우에는 큐에 추가한 뒤 바로 SendAsync메소드를 호출하고
		/// 데이터가 들어있을 경우에는 새로 추가만 한다.
		/// 
		/// 큐잉된 패킷의 전송 시점 :
		///		현재 진행중인 SendAsync가 완료되었을 때 큐를 검사하여 나머지 패킷을 전송한다.
		/// </summary>
		public void Send(ArraySegment<byte> data)
		{
			lock (_csSendingQueue)
			{
				//ConsoleHelper.WriteDefaultLine("lock : " + Thread.CurrentThread.ManagedThreadId.ToString());
				_sendingList.Add(data);

				if (_sendingList.Count > 1)
				{
					// 큐에 무언가가 들어 있다면 아직 이전 전송이 완료되지 않은 상태이므로 큐에 추가만 하고 리턴한다.
					// 현재 수행중인 SendAsync가 완료된 이후에 큐를 검사하여 데이터가 있으면 SendAsync를 호출하여 전송해줄 것이다.
					//ConsoleHelper.WriteDefaultLine("unlock : " + Thread.CurrentThread.ManagedThreadId.ToString());
					return;
				}
				//ConsoleHelper.WriteDefaultLine("unlock : " + Thread.CurrentThread.ManagedThreadId.ToString());
			}

			StartSend();
		}

		public void Send(Packet msg)
		{
			msg.RecordSize();
			Send(new ArraySegment<byte>(msg.Buffer, 0, msg.Position));
		}
		
		/// <summary>
		/// 비동기 전송을 시작한다
		/// </summary>
		private void StartSend()
		{
			try
			{
				// 성능 향상을 위해 SetBuffer에서 BufferList를 사용하는 방식으로 변경함.
				SendEventArgs.BufferList = _sendingList;
				// 비동기 전송 시작.
				bool pending = Socket.SendAsync(SendEventArgs);
				if (!pending)
				{
					ProcessSend(SendEventArgs);
				}
			}
			catch (Exception e)
			{
				if (Socket == null)
				{
					Close();
					return;
				}

				Console.WriteLine("Send error!! close socket. " + e.Message);
				throw new Exception(e.Message, e);
			}
		}
		
		/// <summary>
		/// 비동기 전송 완료시 호출되는 콜백 메소드.
		/// 기존 FreeNet 코드에 치명적인 버그가 있어서 수정했다. BytesTransferred 값이 0일 때가 빈번히 발생하는데
		/// 그럴 때마다 예외처리 없이 return 해 버리는 바람에 더 이상 전송로직이 작동하지 않는 문제가 있었다.
		/// 이를 해결하기 위해 특별히 다른 SocketError가 발생한게 아니라면 재전송을 시도하도록 하였다.
		/// </summary>
		public void ProcessSend(SocketAsyncEventArgs e)
		{
			lock (_csSendingQueue)
			{
				if (e.BytesTransferred <= 0 || e.SocketError != SocketError.Success)
				{
					// Send가 멎어버리는 문제의 원인이 이 부분. SocketError == Success 라면 재전송을 시도한다.

					// SocketError == Success, BytesTransferred == 0 인 에러는 자주발생하므로 로그 출력 안함.
					if (e.BytesTransferred > 0 || e.SocketError != SocketError.Success)
						Console.WriteLine(string.Format("Failed to send. error {0}, transferred {1}", e.SocketError, e.BytesTransferred));
					
					//_sendingList[0] = new ArraySegment<byte>(e.BufferList[0].Array, 0, e.BufferList[0].Array.Length);
					if (e.SocketError != SocketError.Success) return;
				}

				// 리스트에 들어있는 데이터의 총 바이트 수.
				var size = _sendingList.Sum(obj => obj.Count);

				// 전송이 완료되기 전에 추가 전송 요청을 했다면 sending_list에 무언가 더 들어있을 것이다.
				if (e.BytesTransferred != size)
				{
					// 기존코드
					//todo:세그먼트 하나를 다 못보낸 경우에 대한 처리도 해줘야 함.
					// 일단 close시킴.
					//if (e.BytesTransferred < _sendingList[0].Count)
					//{
					//	string error = string.Format("Need to send more! transferred {0},  packet size {1}", e.BytesTransferred, size);
					//	Console.WriteLine(error);

					//	Close();
					//	return;
					//}

					// 수정한 코드
					// 꽤 자주 이 값이 0일 때가 있다. 그 때에는 다시 전송을 시도한다.
					if (e.BytesTransferred > 0)
					{
						// 보낸 만큼 리스트에서 뺀다.
						int sentIndex = 0;
						int sum = 0;
						for (int i = 0; i < _sendingList.Count; ++i)
						{
							sum += _sendingList[i].Count;
							if (sum <= e.BytesTransferred)
							{
								// 여기 까지는 전송 완료된 데이터 인덱스.
								sentIndex = i;
								continue;
							}

							break;
						}
						// 전송 완료된것은 리스트에서 삭제한다.
						_sendingList.RemoveRange(0, sentIndex + 1);
					}
					
					StartSend();
					return;
				}

				// 다 보냈고 더이상 보낼것도 없다.
				_sendingList.Clear();

				// 종료가 예약된 경우, 보낼건 다 보냈으니 진짜 종료 처리를 진행한다.
				if (State == TokenState.ReserveClosing)
				{
					Socket.Shutdown(SocketShutdown.Send);
					// 이쪽에서 끊은 경우에도 호출
					_peer.OnRemoved();
				}
			}
		}

		/// <summary>
		/// 연결을 종료한다.
		/// 주로 클라이언트에서 종료할 때 호출한다.
		/// </summary>
		public void Disconnect()
		{
			// close the socket associated with the client
			try
			{
				if (_sendingList.Count <= 0)
				{
					Socket.Shutdown(SocketShutdown.Send);
					return;
				}

				State = TokenState.ReserveClosing;
			}
			// throws if client process has already closed
			catch (Exception)
			{
				Close();
			}
		}

		/// <summary>
		/// 연결을 종료한다. 단, 종료코드를 전송한 뒤 상대방이 먼저 연결을 끊게 한다.
		/// 주로 서버에서 클라이언트의 연결을 끊을 때 사용한다.
		/// 
		/// TIME_WAIT상태를 서버에 남기지 않으려면 disconnect대신 이 매소드를 사용해서
		/// 클라이언트를 종료시켜야 한다.
		/// </summary>
		public void Ban()
		{
			try
			{
				Byebye();
			}
			catch (Exception)
			{
				Close();
			}
		}

		/// <summary>
		/// 종료코드를 전송하여 상대방이 먼저 끊도록 한다.
		/// </summary>
		private void Byebye()
		{
			Packet bye = Packet.Create(SYS_CLOSE_REQ);
			Send(bye);
		}

		public bool IsConnected()
		{
			return State == TokenState.Connected;
		}


		public void StartHeartbeat()
		{
			if (_heartbeatSender != null)
			{
				_heartbeatSender.Play();
			}
		}


		public void StopHeartbeat()
		{
			if (_heartbeatSender != null)
			{
				_heartbeatSender.Stop();
			}
		}


		public void DisableAutoHeartbeat()
		{
			StopHeartbeat();
			_autoHeartbeat = false;
		}


		public void UpdateHeartbeatManually(float time)
		{
			if (_heartbeatSender != null)
			{
				_heartbeatSender.Update(time);
			}
		}
	}
}

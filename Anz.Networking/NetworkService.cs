using System;
using System.Net.Sockets;

namespace Anz.Networking
{
	public class NetworkService
	{
		// 메시지 수신, 전송시 필요한 객체
		private SocketAsyncEventArgsPool _receiveEventArgsPool;
		private SocketAsyncEventArgsPool _sendEventArgsPool;

		private ClientListener _listener;
		
		// 클라이언트의 접속이 이루어졌을 때 호출되는 대리자
		public delegate void SessionHandler(UserToken token);
		public SessionHandler SessionCreatedCallback { get; set; }

		public LogicMessageEntry LogicEntry { get; private set; }
		public ServerUserManager UserManager { get; private set; }

		/// <summary>
		/// 로직 스레드를 사용하려면 use_logicthread를 true로 설정한다.
		///  -> 하나의 로직 스레드를 생성한다.
		///  -> 메시지는 큐잉되어 싱글 스레드에서 처리된다.
		/// 
		/// 로직 스레드를 사용하지 않으려면 use_logicthread를 false로 설정한다.
		///  -> 별도의 로직 스레드는 생성하지 않는다.
		///  -> IO스레드에서 직접 메시지 처리를 담당하게 된다.
		/// </summary>
		/// <param name="use_logicthread">true=Create single logic thread. false=Not use any logic thread.</param>
		public NetworkService(bool userLogicThread = false)
		{
			SessionCreatedCallback = null;
			UserManager = new ServerUserManager();

			if (userLogicThread)
			{
				LogicEntry = new LogicMessageEntry(this);
				LogicEntry.Start();
			}
		}

		public void Initialize(int maxConnections)
		{
			// configs.
			int bufferSize = Packet.BUFFER_SIZE;
			Initialize(maxConnections, bufferSize);
		}

		// Initializes the server by preallocating reusable buffers and
		// context objects. These objects do not need to be preallocated
		// or reused, but it is done this way to illustrate how the API can
		// easily be used to create reusable objects to increase server performance.
		//
		public void Initialize(int maxConnections, int bufferSize)
		{
			// receive버퍼만 할당해 놓는다.
			// send버퍼는 보낼때마다 할당하든 풀에서 얻어오든 하기 때문에.
			int preAllocCount = 1;

			BufferManager bufferManager = new BufferManager(maxConnections * bufferSize * preAllocCount, bufferSize);
			_receiveEventArgsPool = new SocketAsyncEventArgsPool(maxConnections);
			_sendEventArgsPool = new SocketAsyncEventArgsPool(maxConnections);

			// Allocates one large byte buffer which all I/O operations use a piece of.  This gaurds 
			// against memory fragmentation
			bufferManager.InitBuffer();

			// preallocate pool of SocketAsyncEventArgs objects
			SocketAsyncEventArgs arg;

			for (int i = 0; i < maxConnections; i++)
			{
				// 더이상 UserToken을 미리 생성해 놓지 않는다.
				// 다수의 클라이언트에서 접속 -> 메시지 송수신 -> 접속 해제를 반복할 경우 문제가 생김.
				// 일단 on_new_client에서 그때 그때 생성하도록 하고,
				// 소켓이 종료되면 null로 세팅하여 오류 발생시 확실히 드러날 수 있도록 코드를 변경한다.

				// receive pool
				{
					//Pre-allocate a set of reusable SocketAsyncEventArgs
					arg = new SocketAsyncEventArgs();
					arg.Completed += new EventHandler<SocketAsyncEventArgs>(ReceiveCompleted);
					arg.UserToken = null;

					// assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
					bufferManager.SetBuffer(arg);

					// add SocketAsyncEventArg to the pool
					_receiveEventArgsPool.Push(arg);
				}


				// send pool
				{
					//Pre-allocate a set of reusable SocketAsyncEventArgs
					arg = new SocketAsyncEventArgs();
					arg.Completed += new EventHandler<SocketAsyncEventArgs>(SendCompleted);
					arg.UserToken = null;

					// send버퍼는 보낼때 설정한다. SetBuffer가 아닌 BufferList를 사용.
					arg.SetBuffer(null, 0, 0);

					// add SocketAsyncEventArg to the pool
					_sendEventArgsPool.Push(arg);
				}
			}
		}

		/// <summary>
		/// 포트번호로 0을 주면 사용 가능한 포트가 자동으로 할당된다
		/// </summary>
		/// <returns>할당된 포트번호</returns>
		public int Listen(string host, int port, int backlog)
		{
			_listener = new ClientListener();
			_listener.onNewClient += OnNewClient;
			int assignedPort = _listener.Start(host, port, backlog);

			// heartbeat.
			//byte checkInterval = 10;
			//UserManager.StartHeartbeatChecking(checkInterval, checkInterval);

			return assignedPort;
		}

		public void EndListen()
		{
			if (_listener != null)
			{
				_listener.Stop();
			}
		}

		public void DisableHeartbeat()
		{
			UserManager.StopHeartbeatChecking();
		}

		/// <summary>
		/// 원격 서버에 접속 성공했을 때 호출된다.
		/// </summary>
		public void OnConnectCompleted(Socket serverSocket, UserToken token)
		{
			// SocketAsyncEventArgsPool에서 빼오지 않고 그때그때 할당해서 사용한다.
			// 풀은 서버에서 클라이언트와의 통신용으로만 쓰려고 만든것이기 때문.
			// 클라이언트 입장에서 서버와 통신을 할 때는 접속한 서버당 두개의 EventArgs만 있으면 되기때문에 그냥 new해서 쓴다.
			// 서버간 연결에서도 마찬가지이다.
			// 풀링처리를 하려면 c->s로 가는 별도의 풀을 만들어서 써야한다.
			SocketAsyncEventArgs receiveEventArg = new SocketAsyncEventArgs();
			receiveEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(ReceiveCompleted);
			receiveEventArg.UserToken = token;
			receiveEventArg.SetBuffer(new byte[Packet.BUFFER_SIZE], 0, Packet.BUFFER_SIZE);

			SocketAsyncEventArgs sendEventArg = new SocketAsyncEventArgs();
			sendEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(SendCompleted);
			sendEventArg.UserToken = token;
			sendEventArg.SetBuffer(null, 0, 0);

			BeginReceive(serverSocket, receiveEventArg, sendEventArg);
		}

		/// <summary>
		/// 새로운 클라이언트가 접속 성공했을 때 호출된다.
		/// AcceptAsync의 콜백 메소드에서 호출되며 여러 스레드에서 동시에 호출될 수 있기때문에 공유자원에 접근할 때는 주의해야 한다.
		/// </summary>
		private void OnNewClient(Socket clientSocket, object token)
		{
			// 플에서 하나 꺼내와 사용한다.
			SocketAsyncEventArgs receiveArgs = _receiveEventArgsPool.Pop();
			SocketAsyncEventArgs sendArgs = _sendEventArgsPool.Pop();

			// UserToken은 매번 새로 생성하여 깨끗한 인스턴스로 넣어준다.
			UserToken userToken = new UserToken(LogicEntry);
			userToken.onSessionClosed += OnSessionClosed;
			receiveArgs.UserToken = userToken;
			sendArgs.UserToken = userToken;

			// 어째선지 여기에 두면 즉시 연결이 끊어진다...
			//userToken.SetSocket(clientSocket);

			UserManager.Add(userToken);
			
			userToken.OnConnected();
			if (SessionCreatedCallback != null)
			{
				SessionCreatedCallback(userToken);
			}

			BeginReceive(clientSocket, receiveArgs, sendArgs);

			Packet msg = Packet.Create(UserToken.SYS_START_HEARTBEAT);
			byte sendInterval = 5;
			msg.Push(sendInterval);
			//userToken.Send(msg);
		}

		private void BeginReceive(Socket socket, SocketAsyncEventArgs receiveArgs, SocketAsyncEventArgs sendArgs)
		{
			// receiveArgs, sendArgs 아무곳에서나 꺼내와도 된다. 둘 다 동일한 UserToken을 물고 있다
			UserToken token = receiveArgs.UserToken as UserToken;
			token.SetEventArgs(receiveArgs, sendArgs);

			// 생성된 클라이언트 소켓을 보관해 놓고 통신할 때 사용한다
			token.SetSocket(socket);

			bool pending = socket.ReceiveAsync(receiveArgs);
			if (!pending)
			{
				ProcessReceive(receiveArgs);
			}
		}

		// This method is called whenever a receive or send operation is completed on a socket
		//
		// SocketAsyncEventArgs associated with the completed receive operation
		private void ReceiveCompleted(object sender, SocketAsyncEventArgs e)
		{
			if (e.LastOperation == SocketAsyncOperation.Receive)
			{
				ProcessReceive(e);
				return;
			}

			throw new ArgumentException("The last operation completed on the socket was not a receive.");
		}

		// This method is called whenever a receive or send operation is completed on a socket
		//
		// SocketAsyncEventArgs associated with the completed send operation
		private void SendCompleted(object sender, SocketAsyncEventArgs e)
		{
			try
			{
				UserToken token = e.UserToken as UserToken;
				token.ProcessSend(e);
			}
			catch (Exception expt)
			{
				ConsoleHelper.WriteDefaultLine(string.Format("SendCompleted : An error occurs. {0}", expt.ToString()));
			}
		}

		// This method is invoked when an asynchronous receive operation completes.
		// If the remote host closed the connection, then the socket is closed.
		//
		private void ProcessReceive(SocketAsyncEventArgs e)
		{
			UserToken token = e.UserToken as UserToken;
			if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
			{
				token.OnReceive(e.Buffer, e.Offset, e.BytesTransferred);

				// Keep receive.
				bool pending = token.Socket.ReceiveAsync(e);
				if (!pending)
				{
					// Oh! stack overflow??
					ProcessReceive(e);
				}
			}
			else
			{
				// 수신 바이트 길이가 0인 것은 상대쪽에서 접속이 끊어진 상황을 의미한다.
				if (e.BytesTransferred != 0)
					ConsoleHelper.WriteColoredLine(string.Format("error {0}, transferred {1}", e.SocketError, e.BytesTransferred), ConsoleColor.DarkRed);

				try
				{
					token.Close();
				}
				catch (Exception expt)
				{
					Console.WriteLine(string.Format("Exception occures while handling receiving error. {0}", expt.ToString()));
				}
			}

			//lock (_csReceiveing)
			//{
			//	// Check if the remote host closed the connection
			//	UserToken token = e.UserToken as UserToken;
			//	//if (!token.SocketAlive) return;

			//	if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
			//	{
			//		// 이후의 작업은 UserToken에 맡긴다
			//		token.OnReceive(e.Buffer, e.Offset, e.BytesTransferred);
			//		// 다음 메시지 수신을 위해서 다시 ReceiveAsync메소드를 호출한다.
			//		bool pending = token.Socket.ReceiveAsync(e);
			//		//if (!pending)
			//		//{
			//		//	ProcessReceive(e);
			//		//}
			//	}
			//	else
			//	{
			//		TokenState status = TokenState.UnknownError;
			//		switch (e.SocketError)
			//		{
			//			case SocketError.HostDown:
			//				if (token.Type == TokenType.ServerToken)
			//				{
			//					status = TokenState.FromServer;
			//				}
			//				else
			//				{
			//					status = TokenState.FromClient;
			//				}
			//				break;
			//		}

			//		ConsoleHelper.WriteColoredLine(string.Format("error {0}, transferred {1}", e.SocketError, e.BytesTransferred), ConsoleColor.DarkRed);
			//		ConsoleHelper.WriteDefaultLine("#####NetworkService.ProcessReceive#####");
			//		CloseClientSocket(token, status);
			//	}
			//}
		}

		private void OnSessionClosed(UserToken token)
		{
			UserManager.Remove(token);

			// Free the SocketAsyncEventArg so they can be reused by another client
			// 버퍼는 반환할 필요가 없다. SocketAsyncEventArg가 버퍼를 물고 있기 때문에
			// 이것을 재사용 할 때 물고 있는 버퍼를 그대로 사용하면 되기 때문이다.
			if (_receiveEventArgsPool != null)
			{
				_receiveEventArgsPool.Push(token.ReceiveEventArgs);
			}

			if (_sendEventArgsPool != null)
			{
				_sendEventArgsPool.Push(token.SendEventArgs);
			}

			token.SetEventArgs(null, null);
		}

		///// <summary>
		///// 카운트를 감소시키고 로그를 출력하기 위한 콜백 메소드. 맘에 안들지만 더 좋은 방법이 안 떠오른다.
		///// </summary>
		///// <param name="token"></param>
		//void OnRemoveClient(UserToken token)
		//{
		//	Interlocked.Decrement(ref _connectedCount);

		//	ConsoleHelper.WriteColoredLine(string.Format("[{0}] A client disconnected. handle {1},  count {2}",
		//		Thread.CurrentThread.ManagedThreadId, token.Socket.Handle,
		//		_connectedCount), ConsoleColor.DarkCyan);
		//}



		//public void CloseClientSocket(UserToken token, TokenState status)
		//{
		//	// Close the socket associated with the client
		//	try
		//	{
		//		token.Socket.Shutdown(SocketShutdown.Both);
		//		//token.Socket.Close();
		//	}
		//	// Throws if client process has already closed
		//	catch (Exception e)
		//	{
		//		ConsoleHelper.WriteColoredLine("Disconnection error : " + e.ToString(), ConsoleColor.DarkRed);
		//	}

		//	// Free the SocketAsyncEventArg so they can be reused by another client
		//	// 버퍼는 반환할 필요가 없다. SocketAsyncEventArg가 버퍼를 물고있기 때문에
		//	// 이것을 재사용 할 때 물고있는 버퍼를 그대로 사용하면 되기 때문이다.
		//	if (_receiveEventArgsPool != null)
		//	{
		//		_receiveEventArgsPool.Push(token.ReceiveEventArgs);
		//	}

		//	if(_sendEventArgsPool != null)
		//	{
		//		_sendEventArgsPool.Push(token.SendEventArgs);
		//	}

		//	token.OnRemoved(status);
		//}
	}
}

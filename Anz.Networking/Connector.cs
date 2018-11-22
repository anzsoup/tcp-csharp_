using System;
using System.Net;
using System.Net.Sockets;

namespace Anz.Networking
{
	/// <summary>
	/// Endpoint 정보를 받아서 서버에 접속한다.
	/// 접속하려는 서버 하나당 인스턴스 한개씩 생성하여 사용하면 된다.
	/// </summary>
	public class Connector
	{
		public delegate void ConnectionSuccessHandler(UserToken token);
		public ConnectionSuccessHandler OnSuccess { get; set; }
		public delegate void ConnectionFailedHandler();
		public ConnectionFailedHandler OnFailed { get; set; }

		// 원격지 서버와의 연결을 위한 소켓.
		Socket _serverSocket;

		NetworkService _service;

		public Connector(NetworkService service)
		{
			_service = service;
			OnSuccess = null;
		}

		public void Connect(IPEndPoint remoteEndpoint)
		{
			_serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			// Disable Nagle algorithm
			_serverSocket.NoDelay = true;

			// 비동기 접속을 위한 event args
			SocketAsyncEventArgs eventArg = new SocketAsyncEventArgs();
			eventArg.Completed += OnConnectCompleted;
			eventArg.RemoteEndPoint = remoteEndpoint;
			bool pending = _serverSocket.ConnectAsync(eventArg);
			if (!pending)
			{
				OnConnectCompleted(null, eventArg);
			}
		}

		private void OnConnectCompleted(object sender, SocketAsyncEventArgs e)
		{
			if(e.SocketError == SocketError.Success)
			{
				//Console.WriteLine("Connect completed!");
				UserToken token = new UserToken(_service.LogicEntry);

				// 데이터 수신 준비
				_service.OnConnectCompleted(_serverSocket, token);

				if(OnSuccess != null)
				{
					OnSuccess(token);
				}
			}
			else
			{
				// failed
				ConsoleHelper.WriteColoredLine(string.Format("Failed to connect. {0}", e.SocketError), ConsoleColor.DarkRed);
				OnFailed();
			}
		}
	}
}

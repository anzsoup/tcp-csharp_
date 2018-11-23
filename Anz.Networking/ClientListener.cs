using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Anz.Networking
{
	public class ClientListener
	{
		// 비동기 Accept를 위한 EventArgs
		private SocketAsyncEventArgs _acceptArgs;

		// 클라이언트의 접속을 처리할 소켓
		private Socket _listenSocket;

		// Accept처리의 순서를 제어하기 위한 이벤트 변수
		private AutoResetEvent _flowControlEvent;

		private bool _shouldDead;

		// 새로운 클라이언트가 접속했을 때 호출되는 콜백
		public delegate void NewClientHandler(Socket clientSocket, object tocken);
		public NewClientHandler onNewClient;

		public ClientListener()
		{
			onNewClient = null;
			_shouldDead = false;
		}
		
		
		public int Start(string host, int port, int backlog)
		{
			// 소켓 생성
			_listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			// TCP는 소켓을 닫아도 커널은 즉시 소멸시키지 않고 몇 초간 유지시킨다.
			// (대기하는 것을 Linger 라고 하고, 그 상태를 TIME_WAIT 이라고 한다.)
			// 이것은 서버를 닫고 얼마 지나지 않아 다시 서버를 열 때 포트 중복으로 인한 문제를 발생시킨다.
			// 그래서 ReuseAddress 옵션을 true 로 설정해 같은 endpoint 에 소켓을 열어도 에러가 발생하지 않게 한다.
			_listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			
			IPAddress address;
			if(host.Equals("0.0.0.0"))
			{
				address = IPAddress.Any;
			}
			else
			{
				address = IPAddress.Parse(host);
			}
			IPEndPoint endpoint = new IPEndPoint(address, port);

			try
			{
				// 소켓에 host정보를 바인딩 시킨 뒤 Listen메소드를 호출하여 준비
				_listenSocket.Bind(endpoint);
				_listenSocket.Listen(backlog);

				 _acceptArgs = new SocketAsyncEventArgs();
				 _acceptArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnAcceptCompleted);

				// 클라이언트가 들어오기를 기다린다
				// 비동기 메소드이므로 블로킹되지 않고 바로 리턴된다
				// 콜백 메소드를 통해서 접속 통보를 처리하면 된다
				//bool pending = _listenSocket.AcceptAsync(_acceptArgs);
				//if(!pending)
				//{
				//	OnAcceptCompleted(null, _acceptArgs);
				//}
				
				// 위의 방식은 특정 OS버전에서 콘솔 입력이 대기중일 때 accept처리가 안되는 버그가 있다고 하므로
				// 직접 별도의 스레드를 생성한다
				Thread listenThread = new Thread(DoListen);
				listenThread.Start();
			}
			catch(Exception e)
			{
				ConsoleHelper.WriteColoredLine(e.Message, ConsoleColor.DarkRed);
			}

			// 0을 입력해 자동 할당했을 경우를 위해 리스너 소켓의 포트번호를 반환
			return ((IPEndPoint)_listenSocket.LocalEndPoint).Port;
		}

		/// <summary>
		/// 루프를 돌며 클라이언트를 받아들인다.
		/// 하나의 접속 처리가 완료된 후 다음 Accept를 수행하기 위해서 Event객체를 통해 흐름을 제어하도록 구현되어 있다.
		/// </summary>
		void DoListen()
		{
			// Accept 처리 제어를 위해 이벤트 객체를 생성
			_flowControlEvent = new AutoResetEvent(false);

			while(!_shouldDead)
			{
				// SocketAsyncEventArgs를 재사용하기 위해서 null로 만들어 준다
				_acceptArgs.AcceptSocket = null;

				bool pending = true;
				try
				{
					// 비동기 accept를 호출하여 클라이언트의 접속을 받아들인다
					// 비동기 메소드이지만 동기적으로 수행이 완료될 경우도 있으니
					// 리턴값을 확인하여 분기시켜야 한다
					pending = _listenSocket.AcceptAsync(_acceptArgs);
				}
				catch(Exception e)
				{
					ConsoleHelper.WriteColoredLine(e.Message, ConsoleColor.DarkRed);
					continue;
				}

				// 즉시 완료되면 이벤트가 발생하지 않으므로 리턴값이 false일 경우 콜백 메소드를 직접 호출
				// pending상태라면 비동기 요청이 들어간 상태이므로 콜백 메소드를 기다리면 된다
				// http://msdn.microsoft.com/ko-kr/library/system.net.sockets.socket.acceptasync%28v=vs.110%29.aspx
				if(!pending)
				{
					OnAcceptCompleted(null, _acceptArgs);
				}

				// 클라이언트 접속 처리가 완료되면 이벤트 객체의 신호를 전달받아 다시 루프를 수행하도록 한다
				_flowControlEvent.WaitOne();

				// *팁 : 반드시 WaitOne -> Set 순서로 호출 되야 하는 것은 아니다.
				//      Accept작업이 굉장히 빨리 끝나서 Set -> WaitOne 순서로 호출된다고 하더라도 
				//      다음 Accept 호출 까지 문제 없이 이루어 진다.
				//      WaitOne매소드가 호출될 때 이벤트 객체가 이미 signalled 상태라면 스레드를 대기 하지 않고 계속 진행하기 때문.
			}
		}

		/// <summary>
		/// AcceptAsync의 콜백 메소드
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e">AcceptAsync 메소드 호출시 사용된 EventArgs</param>
		void OnAcceptCompleted(object sender, SocketAsyncEventArgs e)
		{
			if (e.SocketError == SocketError.Success)
			{
				// 새로 생긴 소켓을 보관
				Socket clientSocket = e.AcceptSocket;
				clientSocket.NoDelay = true;

				// 이 클래스에서는 Accept까지의 역할만 수행하고 클라이언트의 접속 이후의 처리는
				// 외부로 넘기기 위해서 콜백 메소드를 호출해 준다
				// 이유는 소켓 처리부와 컨텐츠 구현부를 분리하기 위함이다
				// 컨텐츠 구현부분은 자주 바뀔 가능성이 있지만, 소켓 Accept부분은 상대적으로 변경이 적은 부분이기 때문
				if(onNewClient != null)
				{
					onNewClient(clientSocket, e.UserToken);
				}

				// 다음 연결을 받아들인다
				_flowControlEvent.Set();

				return;
			}
			else
			{
				// Accept 실패 처리
				ConsoleHelper.WriteColoredLine("Failed to accept client", ConsoleColor.DarkRed);
				ConsoleHelper.WriteDefaultLine(e.SocketError.ToString());
			}

			// 다음 연결을 받아들인다
			_flowControlEvent.Set();
		}

		/// <summary>
		/// 클라이언트 리스너 스레드를 종료한다.
		/// </summary>
		public void Stop()
		{
			// 리스너 스레드의 while 루프를 빠져나오게 한다.
			// 스레드 대기중일 경우 while 루프를 빠져나오지 못하므로 Set해주어야 한다.
			_shouldDead = true;
			if (_flowControlEvent != null)
			{
				_flowControlEvent.Set();
			}
		}
	}
}

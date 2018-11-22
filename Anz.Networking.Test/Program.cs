using System;
using System.Net;
using System.Threading;

namespace Anz.Networking.Test
{
	class Program
	{
		static ManualResetEvent manualEvent = new ManualResetEvent(false);
		static RemotePeer serverPeer;
		static RemotePeer clientPeer;

		static void Main(string[] args)
		{
			// 빌드 후 커맨드 라인 인자로 -s 를 주면 서버모드, 
			// -c 를 주면 클라모드로 실행된다.
			// 서버를 먼저 실행하고 이어서 클라를 실행하면
			// 서로 한 번씩 데이터를 주고받고나서 종료된다.

			switch (args[0])
			{
				case "-s":
					RunServer();
					break;

				case "-c":
					RunClient();
					break;

				default:
					Console.WriteLine("Incorrect command line arguments." +
						"\n\t-s: Run Server Mode" +
						"\n\t-c: Run Client Mode");
					break;
			}
		}

		static void RunServer()
		{
			// 클라이언트 리스닝 시작
			var service = new NetworkService();
			// 누군가 접속했을 때 호출되는 콜백
			service.SessionCreatedCallback += OnSessionCreated;
			service.Initialize(10);
			service.Listen("0.0.0.0", 25526, 10);

			// 소켓 관련 콜백 메소드들은 다른 쓰레드에서 호출되므로
			// 메인 쓰레드가 종료되지 않게 대기시킨다.
			manualEvent.WaitOne();
			service.EndListen();
		}

		static void RunClient()
		{
			// 로컬 호스트에 접속
			var service = new NetworkService();
			var connector = new Connector(service);
			// 접속에 성공했을 때 호출되는 콜백
			connector.OnSuccess += OnConnectionSuccess;
			// 접속에 실패했을 때 호출되는 콜백
			connector.OnFailed += OnConnectionFailed;

			IPAddress ipAddress;
			if (!IPAddress.TryParse("127.0.0.1", out ipAddress))
			{
				ipAddress = Dns.GetHostAddresses("127.0.0.1")[0];
			}
			IPEndPoint endpoint = new IPEndPoint(ipAddress, 25526);
			connector.Connect(endpoint);

			// 소켓 관련 콜백 메소드들은 다른 쓰레드에서 호출되므로
			// 메인 쓰레드가 종료되지 않게 대기시킨다.
			manualEvent.WaitOne();
		}

		static void OnSessionCreated(UserToken token)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("Client Connected!!");
			Console.ResetColor();

			// 해당 토큰에 발생하는 네트워크 관련 이벤트를 받으려면
			// IPeer 객체를 만들어야 한다.
			clientPeer = new RemotePeer(token, OnMessageFromClient);
		}

		static void OnConnectionSuccess(UserToken serverToken)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("Connection Succeed!!");
			Console.WriteLine("Send \'Hello, Server!\' to Server and Wait for response.");
			Console.ResetColor();

			// 패킷에 원하는 데이터를 넣는다. 모든 패킷은 반드시 
			// 맨 첫번째에 protocol id 가 있다.
			serverPeer = new RemotePeer(serverToken, OnMessageFromServer);
			var packet = Packet.Create(0);
			packet.Push("Hello, Server!");
			serverPeer.Send(packet);
		}

		static void OnConnectionFailed()
		{
			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.WriteLine("Connection Failed!!");
			Console.ResetColor();
			manualEvent.Set();
		}

		static void OnMessageFromClient(Packet msg)
		{
			// 패킷의 내용물을 순서대로 뽑기 때문에 불필요하더라도
			// protocol id 를 먼저 뽑아야 한다.
			msg.PopProtocolID();
			string message = msg.PopString();
			Console.WriteLine("Message from Client: " + message);

			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("Send \'Hello, Client!\' to Client and terminate.");
			Console.ResetColor();

			var packet = Packet.Create(0);
			packet.Push("Hello, Client!");
			clientPeer.Send(packet);

			manualEvent.Set();
		}

		static void OnMessageFromServer(Packet msg)
		{
			msg.PopProtocolID();
			string message = msg.PopString();
			Console.WriteLine("Message from Server: " + message);
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("Done.");
			Console.ResetColor();

			manualEvent.Set();
		}
	}
}

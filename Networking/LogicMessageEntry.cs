using System;
using System.Collections.Generic;
using System.Threading;

namespace Networking
{
	/// <summary>
	/// 수신된 패킷을 받아 로직 스레드에서 분배하는 역할을 담당한다.
	/// </summary>
	public class LogicMessageEntry : IMessageDispatcher
	{
		private NetworkService _service;
		private ILogicQueue _messageQueue;
		private AutoResetEvent _logicEvent;

		public LogicMessageEntry(NetworkService service)
		{
			_service = service;
			_messageQueue = new DoubleBufferingQueue();
			_logicEvent = new AutoResetEvent(false);
		}

		/// <summary>
		/// 로직 스레드 시작.
		/// </summary>
		public void Start()
		{
			Thread logic = new Thread(DoLogic);
			logic.Start();
		}


		void IMessageDispatcher.OnMessage(UserToken user, ArraySegment<byte> buffer)
		{
			// 여긴 IO스레드에서 호출된다.
			// 완성된 패킷을 메시지큐에 넣어준다.
			Packet msg = new Packet(buffer, user);
			_messageQueue.Enqueue(msg);

			// 로직 스레드를 깨워 일을 시킨다.
			_logicEvent.Set();
		}


		/// <summary>
		/// 로직 스레드.
		/// </summary>
		private void DoLogic()
		{
			while (true)
			{
				// 패킷이 들어오면 알아서 깨워 주겠지.
				_logicEvent.WaitOne();

				// 메시지를 분배한다.
				DispatchAll(_messageQueue.GetAll());
			}
		}


		void DispatchAll(Queue<Packet> queue)
		{
			while (queue.Count > 0)
			{
				Packet msg = queue.Dequeue();
				if (!_service.UserManager.IsExist(msg.Owner))
				{
					continue;
				}

				msg.Owner.OnMessage(msg);
			}
		}
	}
}

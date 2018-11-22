﻿using System.Collections.Generic;

namespace Anz.Networking
{
	/// <summary>
	/// 두개의 큐를 교체해가며 활용한다.
	/// IO스레드에서 입력큐에 막 쌓아놓고,
	/// 로직스레드에서 큐를 뒤바꾼뒤(swap) 쌓아놓은 패킷을 가져가 처리한다.
	/// 참고 : http://roadster.egloos.com/m/4199854
	/// </summary>
	class DoubleBufferingQueue : ILogicQueue
	{
		// 실제 데이터가 들어갈 큐.
		private Queue<Packet> _queue1;
		private Queue<Packet> _queue2;

		// 각각의 큐에 대한 참조.
		private Queue<Packet> _refInput;
		private Queue<Packet> _refOutput;

		private object _csWrite;


		public DoubleBufferingQueue()
		{
			// 초기 세팅은 큐와 참조가 1:1로 매칭되게 설정한다.
			// ref_input - queue1
			// ref)output - queue2
			_queue1 = new Queue<Packet>();
			_queue2 = new Queue<Packet>();
			_refInput = _queue1;
			_refOutput = _queue2;

			_csWrite = new object();
		}


		/// <summary>
		/// IO스레드에서 전달한 패킷을 보관한다.
		/// </summary>
		/// <param name="msg"></param>
		void ILogicQueue.Enqueue(Packet msg)
		{
			lock (_csWrite)
			{
				_refInput.Enqueue(msg);
			}
		}


		Queue<Packet> ILogicQueue.GetAll()
		{
			Swap();
			return _refOutput;
		}


		/// <summary>
		/// 입력큐와 출력큐를 뒤바꾼다.
		/// </summary>
		private void Swap()
		{
			lock (_csWrite)
			{
				Queue<Packet> temp = _refInput;
				_refInput = _refOutput;
				_refOutput = temp;
			}
		}
	}
}

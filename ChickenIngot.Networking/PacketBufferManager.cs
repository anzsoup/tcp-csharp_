using System;
using System.Collections.Generic;

namespace ChickenIngot.Networking
{
	//=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
	// Not stable. Do not use this class!!
	//=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
	public class PacketBufferManager
	{
		static object _csBuffer = new object();
		static Stack<Packet> _pool;
		static int _poolCapacity;

		public static void Initialize(int capacity)
		{
			_pool = new Stack<Packet>();
			_poolCapacity = capacity;
			Allocate();
		}

		static void Allocate()
		{
			for (int i = 0; i < _poolCapacity; ++i)
			{
				_pool.Push(new Packet());
			}
		}

		public static Packet Pop()
		{
			lock(_csBuffer)
			{
				if(_pool.Count <= 0)
				{
					ConsoleHelper.WriteDefaultLine("Reallocate packet pool.");
					Allocate();
				}

				return _pool.Pop();
			}
		}

		public static void Push(Packet packet)
		{
			lock(_csBuffer)
			{
				_pool.Push(packet);
			}
		}
	}
}

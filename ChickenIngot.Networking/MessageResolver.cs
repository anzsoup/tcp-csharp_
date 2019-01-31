using System;

namespace ChickenIngot.Networking
{
	class Defines
	{
		public static readonly short HEADERSIZE = 2;
	}

	public delegate void CompletedMessageCallback(ArraySegment<byte> buffer);

	/// <summary>
	/// [header][body] 구조를 갖는 데이터를 파싱하는 클래스.
	/// - header : 데이터 사이즈. Defines.HEADERSIZE에 정의된 타입만큼의 크기를 갖는다.
	///				2바이트일 경우 Int16, 4바이트는 Int32로 처리하면 된다.
	///				본문의 크기가 Int16.Max값을 넘지 않는다면 2바이트로 처리하는것이 좋을것 같다.
	///	- body : 메시지 본문.
	/// </summary>
	public class MessageResolver
	{	
		// 메시지 사이즈
		private int _messageSize;

		// 진행중인 버퍼
		private byte[] _messageBuffer = new byte[Packet.BUFFER_SIZE];

		// 현재 진행중인 버퍼의 인덱스를 가리키는 변수.
		// 패킷 하나를 완성한 뒤에는 0으로 초기화 시켜줘야 한다.
		private int _currentPosition;

		// 읽어와야 할 목표 위치
		private int _positionToRead;

		// 남은 사이즈
		private int _remainBytes;

		public MessageResolver()
		{
			_messageSize = 0;
			_currentPosition = 0;
			_positionToRead = 0;
			_remainBytes = 0;
		}

		/// <summary>
		/// 목표지점으로 설정된 위치까지의 바이트를 원본 버퍼로부터 복사한다.
		/// 데이터가 모자랄 경우 현재 남은 바이트 까지만 복사한다.
		/// </summary>
		/// <param name="buffer"></param>
		/// <param name="size_to_read"></param>
		/// <returns>다 읽었으면 true, 데이터가 모자라서 못 읽었으면 false를 리턴한다.</returns>
		private bool ReadUntil(byte[] buffer, ref int srcPosition)
		{
			// 읽어와야 할 바이트.
			// 데이터가 분리되어 올 경우 이전에 읽어놓은 값을 빼줘서 부족한만큼 읽어올 수 있도록 계산해 준다
			int copySize = _positionToRead - _currentPosition;

			// 남은 데이터가 더 적다면 가능한 만큼만 복사한다
			if (_remainBytes < copySize)
			{
				copySize = _remainBytes;
			}

			// 버퍼에 복사
			Array.Copy(buffer, srcPosition, _messageBuffer, _currentPosition, copySize);

			// 원본 버퍼 포지션 이동
			srcPosition += copySize;

			// 타겟 버퍼 포지션도 이동
			_currentPosition += copySize;

			// 남은 바이트 수
			_remainBytes -= copySize;

			// 목표지점에 도달 못했으면 false
			if (_currentPosition < _positionToRead)
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// 소켓 버퍼로부터 데이터를 수신할 때마다 호출된다.
		/// 데이터가 남아 있을 때까지 계속 패킷을 만들어 callback을 호출해 준다.
		/// 하나의 패킷을 완성하지 못했다면 버퍼에 보관해 놓은 뒤 다음 수신을 기다린다.
		/// </summary>
		public void OnReceive(byte[] buffer, int offset, int transferred, CompletedMessageCallback callback)
		{
			// 이번 receive로 읽어오게 될 바이트 수.
			_remainBytes = transferred;

			// 원본 버퍼의 포지션값.
			// 패킷이 여러개 뭉쳐 올 경우 원본 버퍼의 포지션은 계속 앞으로 가야 하는데 그 처리를 위한 변수이다.
			int srcPosition = offset;

			// 남은 데이터가 있다면 계속 반복한다.
			while (_remainBytes > 0)
			{
				bool completed = false;

				// 헤더만큼 못읽은 경우 헤더를 먼저 읽는다.
				if (_currentPosition < Defines.HEADERSIZE)
				{
					// 목표 지점 설정(헤더 위치까지 도달하도록 설정).
					_positionToRead = Defines.HEADERSIZE;

					completed = ReadUntil(buffer, ref srcPosition);
					if (!completed)
					{
						// 아직 다 못읽었으므로 다음 receive를 기다린다.
						return;
					}

					// 헤더 하나를 온전히 읽어왔으므로 메시지 사이즈를 구한다.
					_messageSize = GetTotalMessageSize();

					// 메시지 사이즈가 0이하라면 잘못된 패킷으로 처리한다.
					// It was wrong message if size less than zero.
					if (_messageSize <= 0)
					{
						ClearBuffer();
						return;
					}

					// 다음 목표 지점.
					// 패킷 헤더에 기록된 body size = 헤더길이를 제외한 메시지 사이즈.
					// 따라서 패킷 전체 길이를 알려면 헤더길이를 더해줘야 함.
					_positionToRead = _messageSize + Defines.HEADERSIZE;

					// 헤더를 다 읽었는데 더이상 가져올 데이터가 없다면 다음 receive를 기다린다.
					// (예를들어 데이터가 조각나서 헤더만 오고 메시지는 다음번에 올 경우)
					if (_remainBytes <= 0)
					{
						return;
					}
				}

				// 메시지를 읽는다.
				completed = ReadUntil(buffer, ref srcPosition);

				if (completed)
				{
					// 패킷 하나를 완성 했다.
					byte[] clone = new byte[_positionToRead];
					Array.Copy(_messageBuffer, clone, _positionToRead);
					ClearBuffer();
					callback(new ArraySegment<byte>(clone, 0, _positionToRead));
				}
			}
		}

		/// <summary>
		/// 헤더+바디 사이즈를 구한다.
		/// 패킷 헤더부분에 이미 전체 메시지 사이즈가 계산되어 있으므로 헤더 크기에 맞게 변환만 시켜주면 된다.
		/// </summary>
		/// <returns></returns>
		private int GetTotalMessageSize()
		{
			if (Defines.HEADERSIZE == 2)
			{
				return BitConverter.ToInt16(_messageBuffer, 0);
			}
			else if (Defines.HEADERSIZE == 4)
			{
				return BitConverter.ToInt32(_messageBuffer, 0);
			}

			return 0;
		}

		public void ClearBuffer()
		{
			Array.Clear(_messageBuffer, 0, _messageBuffer.Length);

			_currentPosition = 0;
			_messageSize = 0;
		}
	}
}

using System;
using System.Text;

namespace Anz.Networking
{
	/// <summary>
	/// byte[] 버퍼를 참조로 보관하여 PopXXX 메소드 호출 순서대로 데이터 변환을 수행한다.
	/// </summary>
	public class Packet
	{
		/// 패킷의 최대 사이즈는 몇이 좋을까?
		/// 키워드 : Segmentation
		/// http://nenunena.tistory.com/60
		/// 
		public static readonly int BUFFER_SIZE = 1440;
		
		public UserToken Owner { get; private set; }
		public byte[] Buffer { get; private set; }
		public int Position { get; private set; }
		public int Size { get; private set; }
		public Int16 ProtocolID { get; private set; }

		public static Packet Create(Int16 protocolID)
		{
			Packet packet = new Packet();
			//todo:CPacketBufferManager 리팩토링
			//CPacket packet = CPacketBufferManager.pop();
			packet.SetProtocol(protocolID);
			return packet;
		}

		public static void Destroy(Packet packet)
		{
			//CPacketBufferManager.push(packet);
		}

		public Packet(ArraySegment<byte> buffer, UserToken owner)
		{
			// 참조로만 보관하여 작업한다.
			// 복사가 필요하면 별도로 구현해야 한다.
			Buffer = buffer.Array;

			// 헤더는 읽을필요 없으니 그 이후부터 시작한다.
			Position = Defines.HEADERSIZE;
			Size = buffer.Count;

			// 프로토콜 아이디만 확인할 경우도 있으므로 미리 뽑아놓는다.
			ProtocolID = PopProtocolID();
			Position = Defines.HEADERSIZE;

			Owner = owner;
		}

		public Packet(byte[] buffer, UserToken owner)
		{
			// 참조로만 보관하여 작업한다.
			// 복사가 필요하면 별도로 구현해야 한다.
			Buffer = buffer;

			// 헤더는 읽을필요 없으니 그 이후부터 시작한다.
			Position = Defines.HEADERSIZE;

			Owner = owner;
		}

		public Packet()
		{
			Buffer = new byte[BUFFER_SIZE];
			Position = Defines.HEADERSIZE;
		}

		public void CopyTo(Packet target)
		{
			target.SetProtocol(ProtocolID);
			target.Overwrite(Buffer, Position);
		}

		private void Overwrite(byte[] source, int position)
		{
			Array.Copy(source, Buffer, source.Length);
			Position = position;
		}

		public Int16 PopProtocolID()
		{
			return PopInt16();
		}

		public bool PopBool()
		{
			Int16 data = PopInt16();
			if (data == (Int16)1) return true;
			else if (data == (Int16)0) return false;

			return false;
		}

		public Int16 PopInt16()
		{
			Int16 data = BitConverter.ToInt16(Buffer, Position);
			Position += sizeof(Int16);
			return data;
		}

		public Int32 PopInt32()
		{
			Int32 data = BitConverter.ToInt32(Buffer, Position);
			Position += sizeof(Int32);
			return data;
		}

		public float PopSingle()
		{
			float data = BitConverter.ToSingle(Buffer, Position);
			Position += sizeof(float);
			return data;
		}

		public string PopString()
		{
			// 문자열 길이는 최대 2바이트까지. 0 ~ 32767
			Int16 len = BitConverter.ToInt16(Buffer, Position);
			Position += sizeof(Int16);

			// 인코딩은 utf8로 통일한다
			string data = System.Text.Encoding.UTF8.GetString(Buffer, Position, len);
			Position += len;

			return data;
		}

		public byte PopByte()
		{
			byte data = Buffer[Position];
			Position += sizeof(byte);
			return data;
		}

		public byte[] PopByteArray()
		{
			Int16 len = BitConverter.ToInt16(Buffer, Position);
			Position += sizeof(Int16);

			byte[] data = new byte[len];
			Array.Copy(Buffer, Position, data, 0, len);
			Position += len;

			return data;
		}

		public void SetProtocol(Int16 protocolID)
		{
			ProtocolID = protocolID;
			//Buffer = new byte[1024];

			// 헤더는 나중에 넣을것이므로 데이터부터 넣을 수 있도록 위치를 점프시켜놓는다
			Position = Defines.HEADERSIZE;

			PushInt16(protocolID);
		}

		public void RecordSize()
		{
			Int16 bodySize = (Int16)(Position - Defines.HEADERSIZE);
			byte[] header = BitConverter.GetBytes(bodySize);
			header.CopyTo(Buffer, 0);
		}

		public void PushInt16(Int16 data)
		{
			byte[] tempBuffer = BitConverter.GetBytes(data);
			tempBuffer.CopyTo(Buffer, Position);
			Position += tempBuffer.Length;
			Size += tempBuffer.Length;
		}

		public void Push(bool data)
		{
			if (data)
				PushInt16((Int16)1);
			else
				PushInt16((Int16)0);
		}

		public void Push(Int32 data)
		{
			byte[] tempBuffer = BitConverter.GetBytes(data);
			tempBuffer.CopyTo(Buffer, Position);
			Position += tempBuffer.Length;
			Size += tempBuffer.Length;
		}

		public void Push(byte data)
		{
			byte[] tempBuffer = new byte[] { data };
			tempBuffer.CopyTo(Buffer, Position);
			Position += sizeof(byte);
			Size += sizeof(byte);
		}

		public void PushSingle(float data)
		{
			byte[] tempBuffer = BitConverter.GetBytes(data);
			tempBuffer.CopyTo(Buffer, Position);
			Position += tempBuffer.Length;
			Size += tempBuffer.Length;
		}

		public void Push(string data)
		{
			if (data == null) data = "";
			byte[] tempBuffer = Encoding.UTF8.GetBytes(data);

			Int16 len = (Int16)tempBuffer.Length;
			byte[] lenBuffer = BitConverter.GetBytes(len);
			lenBuffer.CopyTo(Buffer, Position);
			Position += sizeof(Int16);
			Size += sizeof(Int16);

			tempBuffer.CopyTo(Buffer, Position);
			Position += tempBuffer.Length;
			Size += tempBuffer.Length;
		}

		public void PushByteArray(byte[] data)
		{
			Int16 len = (Int16)data.Length;
			byte[] lenBuffer = BitConverter.GetBytes(len);
			lenBuffer.CopyTo(Buffer, Position);
			Position += sizeof(Int16);
			Size += sizeof(Int16);

			data.CopyTo(Buffer, Position);
			Position += data.Length;
			Size += data.Length;
		}
	}
}

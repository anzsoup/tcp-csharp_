using System;
using System.Collections.Generic;
using System.Threading;

namespace Anz.Networking
{
	/// <summary>
	/// 현재 접속중인 전체 유저를 관리하는 클래스.
	/// </summary>
	public class ServerUserManager
	{
		private object _csUser;
		private List<UserToken> _userList;

		private Timer _timerHeartbeat;
		private long _heartbeatDuration;


		public ServerUserManager()
		{
			_csUser = new object();
			_userList = new List<UserToken>();
		}


		public void StartHeartbeatChecking(uint checkIntervalSec, uint allowDurationSec)
		{
			_heartbeatDuration = allowDurationSec * 10000000;
			_timerHeartbeat = new Timer(CheckHeartbeat, null, 1000 * checkIntervalSec, 1000 * checkIntervalSec);
		}


		public void StopHeartbeatChecking()
		{
			_timerHeartbeat.Dispose();
		}


		public void Add(UserToken user)
		{
			lock (_csUser)
			{
				_userList.Add(user);
			}
		}


		public void Remove(UserToken user)
		{
			lock (_csUser)
			{
				_userList.Remove(user);
			}
		}


		public bool IsExist(UserToken user)
		{
			lock (_csUser)
			{
				return this._userList.Exists(obj => obj == user);
			}
		}


		public int GetTotalCount()
		{
			return _userList.Count;
		}


		void CheckHeartbeat(object state)
		{
			long allowedTime = DateTime.Now.Ticks - _heartbeatDuration;

			lock (_csUser)
			{
				for (int i = 0; i < _userList.Count; ++i)
				{
					long heartbeatTime = _userList[i].LatestHeartbeatTime;
					if (heartbeatTime >= allowedTime)
					{
						continue;
					}

					_userList[i].Disconnect();
				}
			}
		}


	}
}

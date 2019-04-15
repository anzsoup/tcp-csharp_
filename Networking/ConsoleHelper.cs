using System;

namespace Networking
{
	public class ConsoleHelper
	{
		public static void WriteColoredLine(string msg, ConsoleColor color)
		{
			Console.ForegroundColor = color;
			Console.WriteLine(msg);
			Console.ResetColor();
		}

		public static void WriteDefaultLine(string msg)
		{
			Console.ForegroundColor = ConsoleColor.DarkGray;
			Console.WriteLine(msg);
			Console.ResetColor();
		}
	}
}

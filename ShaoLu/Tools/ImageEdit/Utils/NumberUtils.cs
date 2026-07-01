using System;
using System.Collections.Generic;
using System.Text;

namespace ImageTool.Utils
{
    public static class NumberUtils
    {
		#region 数值计算

		public static string GetPercentages(double num,int scale=2) 
		{
			num = num * 100;
			string result = string.Format("{0:F2}%", num);

			return result;
		}

		/// <summary>
		/// 求2个数的百分比
		/// </summary>
		/// <param name="num1"></param>
		/// <param name="num2"></param>
		/// <returns></returns>
		public static string GetRate(string num1, string num2)
		{
			if (string.IsNullOrEmpty(num1) || string.IsNullOrEmpty(num2))
				return string.Empty;

			string result = string.Empty;

			double number1 = Convert.ToInt64(num1);
			double number2 = Convert.ToInt64(num2);

			double number3 = (number1 / number2) * 100.00;
			result = string.Format("{0:F2}%", number3);

			return result;
		}

		/// <summary>
		/// 两个数相加
		/// </summary>
		/// <param name="num1"></param>
		/// <param name="num2"></param>
		/// <returns></returns>
		public static string Add(string num1, string num2)
		{
			if (string.IsNullOrEmpty(num1) || string.IsNullOrEmpty(num2))
				return string.Empty;

			string result = string.Empty;

			int number1 = Convert.ToInt32(num1);
			int number2 = Convert.ToInt32(num2);

			int number3 = number1 + number2;

			result = number3.ToString();

			return result;
		}


		/// <summary>
		/// 两个数相减
		/// </summary>
		/// <param name="num1"></param>
		/// <param name="num2"></param>
		/// <returns></returns>
		public static string Subtraction(string num1, string num2)
		{
			if (string.IsNullOrEmpty(num1) || string.IsNullOrEmpty(num2))
				return string.Empty;

			string result = string.Empty;

			int number1 = Convert.ToInt32(num1);
			int number2 = Convert.ToInt32(num2);

			int number3 = number1 - number2;

			result = number3.ToString();

			return result;
		}


		/// <summary>
		/// 两个数相除
		/// </summary>
		/// <param name="num1"></param>
		/// <param name="num2"></param>
		/// <returns></returns>
		public static string Division(string num1, string num2)
		{

			if (string.IsNullOrEmpty(num1) || string.IsNullOrEmpty(num2))
				return string.Empty;

			string result = "";

			try
			{
				int number1 = Convert.ToInt32(num1);
				int number2 = Convert.ToInt32(num2);
				double number3 = number1 * 1.00 / number2;
				result = number3.ToString("F2");
			}
			catch (Exception)
			{

			}

			return result;
		}

		#endregion
	}
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.InteropServices;
using System.Reflection;

namespace JocysCom.ClassLibrary.Security
{
	public class TokenHelper
	{

		static int checkSumSize = 4;
		static int securityHashSize = 12;
		static int unlockHashSize = 6;

		/// <summary>
		/// Get current time unit value.
		/// </summary>
		/// <param name="unit">Time unit type.</param>
		/// <returns>Time unit value.</returns>
		public static long GetTimeUnitValue(TimeUnitType unit, DateTime? date = null)
		{
			var d = date.HasValue ? date.Value : DateTime.Now;
			switch (unit)
			{
				case TimeUnitType.Seconds: return (long)d.Subtract(DateTime.MinValue).TotalSeconds;
				case TimeUnitType.Minutes: return (long)d.Subtract(DateTime.MinValue).TotalMinutes;
				case TimeUnitType.Hours: return (long)d.Subtract(DateTime.MinValue).TotalHours;
				case TimeUnitType.Days: return (long)d.Subtract(DateTime.MinValue).TotalDays;
			}
			return 0;
		}

		/// <summary>
		/// Generate password reset/token key which is unique to user. Reset key won't change until user logs in or change password.
		/// </summary>
		/// <param name="id">ID</param>
		/// <param name="password">Password or secure key/hash.</param>
		/// <param name="unit">Time unit type.</param>
		/// <returns></returns>
		public static string GetSecurityToken<T1, T2>(T1 id, T2 password, TimeUnitType unit, string hmacHashKey = null)
		{
			long u = GetTimeUnitValue(unit);
			return GetSecurityToken(id, password, u, hmacHashKey);
		}

		/// <summary>
		/// Get security token.
		/// </summary>
		/// <param name="id">User id (Integer or GUID).</param>
		/// <param name="password">Password or secure key/hash.</param>
		/// <param name="unitValue">Unit value.</param>
		/// <returns></returns>
		public static string GetSecurityToken<T1, T2>(T1 id, T2 password, long unitValue, string hmacHashKey = null)
		{
			byte[] idBytes = ObjectToBytes(id);
			byte[] passwordBytes = ObjectToBytes(password);
			byte[] unitBytes = ObjectToBytes(unitValue);
			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);
			writer.Write(idBytes);
			writer.Write(passwordBytes);
			writer.Write(unitBytes);
			byte[] bytes = stream.ToArray();
			byte[] tokenPrefixBytesFull = Encryption.Current.ComputeHash(bytes).ToByteArray();
			string tokenPrefix = BytesToHex(tokenPrefixBytesFull).Substring(0, securityHashSize).ToUpper();
			byte[] tokenDataBytes = ExclusiveORValue(tokenPrefix, idBytes, hmacHashKey);
			string tokenData = BytesToHex(tokenDataBytes);
			string token = string.Format("{0}{1}", tokenPrefix, tokenData);
			var checksum = Checksum(token);
			string fullToken = string.Format("{0}{1}", checksum, token);
			return fullToken;
		}

		static string Checksum(string token)
		{
			byte[] bytes = Encryption.Current.ComputeHash(token).ToByteArray();
			var checksum = BytesToHex(bytes).Substring(0, checkSumSize).ToUpper();
			return checksum;
		}

		public static bool IsValidChecksum(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return false;
			}
			if (key.Length < (checkSumSize + securityHashSize))
			{
				return false;
			}
			var checksum = key.Substring(0, checkSumSize);
			var token = key.Substring(checkSumSize);
			var isValid = checksum == Checksum(token);
			return isValid;
		}

		/// <summary>
		/// Check if token key is valid.
		/// </summary>
		/// <param name="token">Token to check.</param>
		/// <param name="userId">User id (Integer or GUID).</param>
		/// <param name="password">Password or secure key/hash.</param>
		/// <param name="unit">Time unit type.</param>
		/// <param name="count">How many units in past mus be checked.</param>
		/// <returns>True if token is valid, False if not valid.</returns>
		public static bool CheckSecurityToken<T1, T2>(string token, T1 id, T2 password, TimeUnitType unit = TimeUnitType.None, int count = 0)
		{
			if (!IsValidChecksum(token))
			{
				return false;
			}
			// Time which passed.
			long u = GetTimeUnitValue(unit);
			// If there is no expiry then...
			if (u == 0) return (token == GetSecurityToken(id, password, u));
			// Use bias to solve the issue when token generator time is inaccurate and is set up to 5 [seconds] or 1 [minute|hour|day] in future).
			var bias = unit == TimeUnitType.Seconds ? 5 : 1;
			for (int i = -bias; i < count; i++)
			{
				// If resetKey matches to key for given day then...
				if (token == GetSecurityToken(id, password, u - i)) return true;
			}
			return false;
		}

		#region Unlock Token (Decimal)

		/// <summary>Get decimal unlock code.</summary>
		/// <param name="value">Value which will be used to make token.</param>
		/// <param name="unitType">Time unit type. Can be minutes, hours or days.</param>
		/// <returns></returns>
		public static string GetUnlockToken(string value, TimeUnitType unitType, string hmacHashKey = null)
		{
			var u = GetTimeUnitValue(unitType);
			return GetUnlockTokenByValue(value, u, hmacHashKey);
		}

		/// <summary>Get decimal unlock code.</summary>
		/// <param name="value">Value to hash.</param>
		/// <param name="unitValue">Time value which equals to total Seconds, Minutes, Hours or Days passed from zero date to specific point in time.</param>
		/// <returns></returns>
		private static string GetUnlockTokenByValue(string value, long unitValue, string hmacHashKey = null)
		{
			string passString = string.Format("{0}_{1}", value, unitValue);
			var hash = Encryption.Current.ComputeHash(passString, hmacHashKey).ToByteArray();
			var numb = BitConverter.ToUInt32(hash, 0);
			var text = numb.ToString().PadRight(unlockHashSize, '0').Substring(0, unlockHashSize);
			return text;
		}

		/// <summary>
		/// Check if token is valid.
		/// </summary>
		/// <param name="token">Token to check.</param>
		/// <param name="userId">User id (Integer or GUID).</param>
		/// <param name="password">Password or secure key/hash.</param>
		/// <param name="unit">Time unit type.</param>
		/// <param name="count">How many units in past mus be checked.</param>
		/// <returns>True if token is valid, False if not valid.</returns>
		public static bool CheckUnlockToken(string token, string value, TimeUnitType unit, int count, string hmacHashKey = null)
		{
			// Time which passed.
			long u = GetTimeUnitValue(unit);
			// Check keys for last units and return if token match.
			for (int i = 0; i < count; i++) if (token == GetUnlockTokenByValue(value, u - i, hmacHashKey)) return true;
			// -5 solves the issue when token generator time is inaccurate and is set up to 5 [seconds|minutes|hours|days] in future
			for (int i = -5; i < 0; i++) if (token == GetUnlockTokenByValue(value, u - i, hmacHashKey)) return true;
			return false;
		}

		#endregion

		/// <summary>
		/// Get URL to page. If runs on website then host will be replaced with current request.
		/// </summary>
		/// <param name="token">Token.</param>
		/// <param name="page">Page name. Like "/Login.aspx" or "/LoginReset.aspx"</param>
		/// <returns>URL string.</returns>
		public static string GetUrl(string keyName, string token, Uri url = null)
		{
			var context = System.Web.HttpContext.Current;
			var u = (url == null || context != null)
				? System.Web.HttpContext.Current.Request.Url
				: url;
			var absolutePath = (url != null)
				? url.AbsolutePath
				: u.AbsolutePath;
			var port = u.IsDefaultPort ? "" : ":" + u.Port;
			return string.Format("{0}://{1}{2}{3}?{4}={5}", u.Scheme, u.Host, port, absolutePath, keyName, token);
		}

		public static string GetFullUrl(string absolutePath)
		{
			var context = System.Web.HttpContext.Current;
			if (context == null)
				return null;
			var u = context.Request.Url;
			var port = u.IsDefaultPort ? "" : ":" + u.Port;
			var url = string.Format("{0}://{1}{2}{3}", u.Scheme, u.Host, port, absolutePath);
			return url;
		}

		/// <summary>
		/// Get URL to web application root.
		/// </summary>
		public static string GetApplicationUrl()
		{
			var context = System.Web.HttpContext.Current;
			if (context == null)
				return null;
			return GetFullUrl(context.Request.ApplicationPath).TrimEnd('/');
		}

		/// <summary>
		/// Get Id from token.
		/// </summary>
		/// <typeparam name="T">Id type (Integer or GUID).</typeparam>
		/// <param name="token">Token.</param>
		/// <returns>Id.</returns>
		public static T GetData<T>(string token, string hmacHashKey = null)
		{
			string tokenPrefix = token.Substring(checkSumSize, securityHashSize);
			string tokenData = token.Substring(checkSumSize + securityHashSize);
			var tokenDataBytes = HexToBytes(tokenData);
			var value = ExclusiveORValue(tokenPrefix, tokenDataBytes, hmacHashKey);
			var data = (T)BytesToObject(value, typeof(T));
			return data;
		}

		public static bool TryGetData<T>(string token, out T o, string hmacHashKey = null)
		{
			try
			{
				o = GetData<T>(token, hmacHashKey);
				return true;
			}
			catch (Exception)
			{
				o = default(T);
				return false;
			}
		}

		#region Object To/From Bytes

		public static byte[] ExclusiveORValue(string tokenPrefix, byte[] tokenId, string hmacHashKey = null)
		{
			var keyBytes = Encryption.Current.ComputeHash(tokenPrefix, hmacHashKey).ToByteArray();
			var value = ExclusiveOR(tokenId, keyBytes);
			return value;
		}

		public static byte[] ExclusiveOR(byte[] value, byte[] key)
		{
			byte[] result = new byte[value.Length];
			for (int i = 0; i < value.Length; i++)
			{
				result[i] = (byte)(value[i] ^ key[i % key.Length]);
			}
			return result;
		}

		static string ObjectToHex<T>(T o)
		{
			var bytes = ObjectToBytes(o, typeof(T));
			return BytesToHex(bytes);
		}

		static string BytesToHex(byte[] bytes)
		{
			var hexList = bytes.Select(x => x.ToString("X2"));
			return string.Join("", hexList);
		}

		static byte[] HexToBytes(string hex)
		{
			int offset = 0;
			if ((hex.Length % 2) != 0) return new byte[0];
			byte[] bytes = new byte[(hex.Length - offset) / 2];
			for (int i = 0; i < bytes.Length; i++)
			{
				bytes[i] = byte.Parse(hex.Substring(offset, 2), System.Globalization.NumberStyles.HexNumber);
				offset += 2;
			}
			return bytes;
		}

		static T HexToObject<T>(string hex)
		{
			var bytes = HexToBytes(hex);
			var o = (T)BytesToObject(bytes, typeof(T));
			return o;
		}

		///// <summary>
		///// Convert structure to byte array.
		///// </summary>
		//public static byte[] StructureToBytes<T>(T value)
		//{
		//	var size = Marshal.SizeOf(value);
		//	var bytes = new byte[size];
		//	var handle = default(GCHandle);
		//	try
		//	{
		//		handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
		//		Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
		//	}
		//	finally
		//	{
		//		if (handle.IsAllocated)
		//		{
		//			handle.Free();
		//		}
		//	}
		//	return bytes;
		//}

		//public static T BytesToStructure<T>(byte[] bytes)
		//{
		//	return (T)BytesToStructure(bytes, typeof(T));
		//}

		///// <summary>
		///// Convert byte array to structure.
		///// </summary>
		//public static object BytesToStructure(byte[] bytes, Type type)
		//{
		//	var value = type.IsValueType ? Activator.CreateInstance(type) : null;
		//	var handle = default(GCHandle);
		//	try
		//	{
		//		handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
		//		value = Marshal.PtrToStructure(handle.AddrOfPinnedObject(), type);
		//	}
		//	finally
		//	{
		//		if (handle.IsAllocated)
		//		{
		//			handle.Free();
		//		}
		//	}
		//	return value;
		//}

		/// <summary>
		/// Convert object to byte array.
		/// </summary>
		public static byte[] ObjectToBytes(object value, Type type = null)
		{
			if (value == null) return new byte[0];
			var o = (object)value;
			var t = type ?? value.GetType();
			var typeCode = Type.GetTypeCode(t);
			var stream = new MemoryStream();
			var writer = new BinaryWriter(stream, Encoding.UTF8);
			switch (typeCode)
			{
				case TypeCode.Boolean: writer.Write((Boolean)o); break;
				case TypeCode.Byte: writer.Write((Byte)o); break;
				case TypeCode.Char: writer.Write((Char)o); break;
				case TypeCode.DateTime: writer.Write(((DateTime)o).Ticks); break; // Ticks type is Int64.
				case TypeCode.Decimal: writer.Write((Decimal)o); break;
				case TypeCode.Double: writer.Write((Double)o); break;
				case TypeCode.Int16: writer.Write((Int16)o); break;
				case TypeCode.Int32: writer.Write((Int32)o); break;
				case TypeCode.Int64: writer.Write((Int64)o); break;
				case TypeCode.SByte: writer.Write((SByte)o); break;
				case TypeCode.Single: writer.Write((Single)o); break;
				case TypeCode.String: writer.Write(Encoding.UTF8.GetBytes((string)o)); break;
				case TypeCode.UInt16: writer.Write((UInt16)o); break;
				case TypeCode.UInt32: writer.Write((UInt32)o); break;
				case TypeCode.UInt64: writer.Write((UInt64)o); break;
				default:
					if (t == typeof(byte[])) writer.Write((byte[])o);
					else if (t == typeof(Guid)) writer.Write(((Guid)o).ToByteArray());
					else writer.Write(Serialize(o, o.GetType()));
					break;
			}
			var result = stream.ToArray();
			return result;
		}

		static byte[] Serialize(object o, Type type)
		{

			var list = new List<PropertyInfo>();
			var infos = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
			var stream = new MemoryStream();
			foreach (PropertyInfo pi in infos)
			{
				var value = pi.GetValue(o, null);
				var vBytes = ObjectToBytes(value, pi.PropertyType);
				stream.WriteByte((byte)vBytes.Length);
				stream.Write(vBytes, 0, vBytes.Length);
			}
			var bytes = stream.ToArray();
			return bytes;
		}

		public static T BytesToObject<T>(byte[] bytes)
		{
			return (T)BytesToObject(bytes, typeof(T));
		}

		/// <summary>
		/// Convert byte array to object.
		/// </summary>
		/// <remarks>byte[0] is empty/default value.</remarks>
		static object BytesToObject(byte[] bytes, Type type, int? index = null, int? count = null)
		{
			if (bytes == null)
			{
				return type.IsValueType ? Activator.CreateInstance(type) : null;
			}
			// Return empty value.
			if (bytes.Length == 0)
			{
				if (type == typeof(string)) return string.Empty;
				return type.IsValueType ? Activator.CreateInstance(type) : null;
			}
			var typeCode = Type.GetTypeCode(type);
			var stream = new MemoryStream(bytes, index ?? 0, count ?? bytes.Length);
			var reader = new BinaryReader(stream, Encoding.UTF8);
			object o = type.IsValueType ? Activator.CreateInstance(type) : null;
			switch (typeCode)
			{
				case TypeCode.Boolean: o = reader.ReadBoolean(); break;
				case TypeCode.Char: o = reader.ReadChar(); break;
				case TypeCode.DateTime: o = new DateTime(reader.ReadInt64()); break;
				case TypeCode.Decimal: o = reader.ReadDecimal(); break;
				case TypeCode.Double: o = reader.ReadDouble(); break;
				case TypeCode.SByte: o = reader.ReadSByte(); break;
				case TypeCode.Int16: o = reader.ReadInt16(); break;
				case TypeCode.Int32: o = reader.ReadInt32(); break;
				case TypeCode.Int64: o = reader.ReadInt64(); break;
				case TypeCode.Single: o = reader.ReadSingle(); break;
				case TypeCode.String:
					o = Encoding.UTF8.GetString(bytes, index ?? 0, count ?? bytes.Length);
					break;
				case TypeCode.Byte: o = reader.ReadByte(); break;
				case TypeCode.UInt16: o = reader.ReadUInt16(); break;
				case TypeCode.UInt32: o = reader.ReadUInt32(); break;
				case TypeCode.UInt64: o = reader.ReadUInt64(); break;
				default:
					if (type == typeof(byte[])) o = bytes.ToArray();
					else if (type == typeof(Guid)) o = (new Guid(bytes));
					else o = Deserialize(bytes, type);
					break;
			}
			return o;
		}

		static object Deserialize(byte[] bytes, Type type)
		{
			if (bytes == null || bytes.Length == 0) return null;
			var list = new List<PropertyInfo>();
			var infos = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
			var stream = new MemoryStream(bytes);
			// Don't use 'var' and declare as 'object', because if 'T' is value type then
			// pi.SetValue(o, value, null) will fail to set interal value. 
			object o = Activator.CreateInstance(type);
			foreach (PropertyInfo pi in infos)
			{
				var count = stream.ReadByte();
				var value = BytesToObject(bytes, pi.PropertyType, (int)stream.Position, count);
				stream.Position += count;
				pi.SetValue(o, value, null);
			}
			return o;
		}

		#endregion

		#region Backwards Compatibility

		const int securityHashSizeOld = 16;

		/// <summary>
		/// Check if token key is valid.
		/// </summary>
		/// <param name="token">Token to check.</param>
		/// <param name="userId">User id (Integer or GUID).</param>
		/// <param name="password">Password or secure key/hash.</param>
		/// <param name="unit">Time unit type.</param>
		/// <param name="count">How many units in past mus be checked.</param>
		/// <returns>True if token is valid, False if not valid.</returns>
		public static bool CheckSecurityTokenOld<T1, T2>(string token, T1 id, T2 password, TimeUnitType unit = TimeUnitType.None, int count = 0)
		{
			// Time which passed.
			double u = GetTimeUnitValue(unit);
			// If there is no expiry then...
			if (u == 0) return (token == GetSecurityTokenOld(id, password, u));
			// Use bias to solve the issue when token generator time is inaccurate and is set up to 5 [seconds] or 1 [minute|hour|day] in future).
			var bias = unit == TimeUnitType.Seconds ? 5 : 1;
			for (int i = -bias; i < count; i++)
			{
				// If resetKey matches to key for given day then...
				if (token == GetSecurityTokenOld(id, password, u - i)) return true;
			}
			return false;
		}

		/// <summary>
		/// Get security token.
		/// </summary>
		/// <param name="id">User id (Integer or GUID).</param>
		/// <param name="password">Password or secure key/hash.</param>
		/// <param name="unitValue">Unit value.</param>
		/// <returns></returns>
		public static string GetSecurityTokenOld<T1, T2>(T1 id, T2 password, double unitValue, string hmacHashKey = null)
		{
			byte[] idBytes = ObjectToBytes(id);
			byte[] passwordBytes = ObjectToBytes(password);
			byte[] unitBytes = ObjectToBytes(unitValue);
			MemoryStream stream = new MemoryStream();
			BinaryWriter writer = new BinaryWriter(stream);
			writer.Write(idBytes);
			writer.Write(passwordBytes);
			writer.Write(unitBytes);
			byte[] bytes = stream.ToArray();
			byte[] tokenPrefixBytesFull = Encryption.Current.ComputeHash(bytes).ToByteArray();
			string tokenPrefix = BytesToHex(tokenPrefixBytesFull).Substring(0, securityHashSizeOld).ToUpper();
			byte[] tokenIdBytes = ExclusiveORValue(tokenPrefix, idBytes, hmacHashKey);
			string tokenId = BytesToHex(tokenIdBytes);
			string token = string.Format("{0}{1}", tokenPrefix, tokenId);
			return token;
		}

		/// <summary>
		/// Get Id from token.
		/// </summary>
		/// <typeparam name="T">Id type (Integer or GUID).</typeparam>
		/// <param name="token">Token.</param>
		/// <returns>Id.</returns>
		public static T GetIdOld<T>(string token, string hmacHashKey = null)
		{
			string tokenPrefix = token.Substring(0, securityHashSizeOld);
			string tokenId = token.Substring(securityHashSizeOld);
			var tokenIdBytes = HexToBytes(tokenId);
			var value = ExclusiveORValue(tokenPrefix, tokenIdBytes, hmacHashKey);
			var id = (T)BytesToObject(value, typeof(T));
			return id;
		}

		public static bool TryGetIdOld<T>(string token, out T o, string hmacHashKey = null)
		{
			try
			{
				o = GetIdOld<T>(token, hmacHashKey);
				return true;
			}
			catch (Exception)
			{
				o = default(T);
				return false;
			}
		}

		#endregion

		//public static void SendPasswordResetKey(string username, string password, TimeUnitType unit)
		//{
		//    Uri u = System.Web.HttpContext.Current.Request.Url;
		//    string resetUrl = GetUrl(username, password, unit);
		//    string template = Helper.GetTranslation(TranslationKey.PasswordResetTemplate);
		//    string body = template.Replace("{Username}", user.FullName).Replace("{Host}", u.Host).Replace("{ResetKey}", resetUrl);
		//    string subject = string.Format("Reset your {0} password", u.Host);
		//    Engine.Mail.Current.Send(user.Membership.Email, user.FullName, subject, body, JocysCom.ClassLibrary.Mail.MailTextType.Plain);
		//}

	}
}

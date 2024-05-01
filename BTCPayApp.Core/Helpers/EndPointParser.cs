using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace WalletWasabi.Userfacing;
//from wasabi 
public static class EndPointParser
{
	
	public static IPEndPoint IPEndPoint(this EndPoint endPoint)
	{
		
		if(endPoint is IPEndPoint ipEndPoint)
			return ipEndPoint;
		if(endPoint is not DnsEndPoint dnsEndPoint)
			throw new FormatException($"Invalid endpoint: {endPoint}");
		
		
		var addresses = System.Net.Dns.GetHostAddresses(dnsEndPoint.Host);
		if (addresses.Length == 0)
		{
			throw new ArgumentException(
				"Unable to retrieve address from specified host name.", 
				"hostName"
			);
		}
		return new IPEndPoint(addresses[0], dnsEndPoint.Port); // Port gets validated here.
	}
	
	public static string Host(this EndPoint me)
	{
		if (me is DnsEndPoint dnsEndPoint)
		{
			return dnsEndPoint.Host;
		}
		else if (me is IPEndPoint ipEndPoint)
		{
			return ipEndPoint.Address.ToString();
		}
		else
		{
			throw new FormatException($"Invalid endpoint: {me}");
		}
	}
	
	public static int? Port(this EndPoint me)
	{
		var result = 0;
		if (me is DnsEndPoint dnsEndPoint)
		{
			result =  dnsEndPoint.Port;
		}
		else if (me is IPEndPoint ipEndPoint)
		{
			result =  ipEndPoint.Port;
		}
		else
		{
			throw new FormatException($"Invalid endpoint: {me}");
		}
		if(result <= 0)
		{
			return null;
		}
		return result;
	}
	
	public static string ToString(this EndPoint me, int defaultPort)
	{
		string host = me.Host();
		string port = me.Port()?.ToString() ?? defaultPort.ToString();
		var endPointString = $"{host}:{port}";

		return endPointString;
	}

	/// <param name="defaultPort">If invalid and it's needed to use, then this function returns false.</param>
	public static bool TryParse(string? endPointString, int defaultPort, [NotNullWhen(true)] out EndPoint? endPoint)
	{
		
		if(System.Net.IPEndPoint.TryParse(endPointString, out var ipEndPoint))
		{
			if (ipEndPoint.Port == 0)
			{
				ipEndPoint.Port = defaultPort;
			}
			endPoint = ipEndPoint;
			return true;
		}
		
		endPoint = null;

		try
		{
			if (string.IsNullOrWhiteSpace(endPointString))
			{
				return false;
			}

			endPointString = endPointString.TrimEnd(':', '/');

			var parts = endPointString.Split(':', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().TrimEnd('/').TrimEnd()).ToArray();

			if (parts.Length == 0)
			{
				return false;
			}

			ushort p;
			int port;

			if (parts.Length == 1)
			{
				if (IsValidPort(defaultPort.ToString(), out p))
				{
					port = p;
				}
				else
				{
					return false;
				}
			}
			else if (parts.Length == 2)
			{
				var portString = parts[1];

				if (IsValidPort(portString, out p))
				{
					port = p;
				}
				else
				{
					return false;
				}
			}
			else
			{
				return false;
			}

			var host = parts[0];

			if (host == "localhost")
			{
				host = IPAddress.Loopback.ToString();
			}

			endPoint = new DnsEndPoint(host, port);

			return true;
		}
		catch
		{
			return false;
		}
	}

	// Checks a port is a number within the valid port range (0 - 65535).
	private static bool IsValidPort(string port, out ushort p)
	{
		return ushort.TryParse(port, out p);
	}
}

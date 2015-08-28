//
// socket-related test cases
//
// Authors:
//  Steffen Kiess (s-kiess@web.de)
//
// Copyright (C) 2015 Steffen Kiess
//

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

using Mono.Unix;
using Mono.Unix.Native;

using NUnit.Framework;

namespace MonoTests.Mono.Unix.Native
{
	[TestFixture, Category ("NotDotNet")]
	public class SocketTest {

		string TempFolder;

		[SetUp]
		public void SetUp ()
		{
			TempFolder = Path.Combine (Path.GetTempPath (), this.GetType ().FullName);

			if (Directory.Exists (TempFolder))
				Directory.Delete (TempFolder, true);

			Directory.CreateDirectory (TempFolder);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists (TempFolder))
				Directory.Delete (TempFolder, true);
		}

		// Set a timeout on all sockets to make sure that if a test fails it
		// won't cause the program to hang
		void SetTimeout (int socket)
		{
			var timeout = new Timeval {
				tv_sec = 0,
				tv_usec = 500000,
			};
			if (Syscall.setsockopt (socket, UnixSocketProtocol.SOL_SOCKET, UnixSocketOptionName.SO_RCVTIMEO, timeout) < 0 ||
					Syscall.setsockopt (socket, UnixSocketProtocol.SOL_SOCKET, UnixSocketOptionName.SO_SNDTIMEO, timeout) < 0)
				UnixMarshal.ThrowExceptionForLastError ();
		}

		void WithSocketPair (Action<int, int> f)
		{
			int socket1, socket2;
			if (Syscall.socketpair (UnixAddressFamily.AF_UNIX, UnixSocketType.SOCK_STREAM, 0, out socket1, out socket2) < 0)
				UnixMarshal.ThrowExceptionForLastError ();
			try {
				SetTimeout (socket1);
				SetTimeout (socket2);

				f (socket1, socket2);
			} finally {
				int r0 = Syscall.close (socket1);
				int r1 = Syscall.close (socket2);
				if (r0 < 0 || r1 < 0)
					UnixMarshal.ThrowExceptionForLastError ();
			}
		}

		void WithSockets (UnixAddressFamily af, UnixSocketType type, UnixSocketProtocol protocol, Action<int, int> f)
		{
			int so1, so2;
			if ((so1 = Syscall.socket (af, type, protocol)) < 0)
				UnixMarshal.ThrowExceptionForLastError ();
			try {
				if ((so2 = Syscall.socket (af, type, protocol)) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				try {
					SetTimeout (so1);
					SetTimeout (so2);

					f (so1, so2);
				} finally {
					if (Syscall.close (so2) < 0)
						UnixMarshal.ThrowExceptionForLastError ();
				}
			} finally {
				if (Syscall.close (so1) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
			}
		}

		[Test]
		public void Socket ()
		{
			int socket;
			if ((socket = Syscall.socket (UnixAddressFamily.AF_UNIX, UnixSocketType.SOCK_STREAM, 0)) < 0)
				UnixMarshal.ThrowExceptionForLastError ();

			if (Syscall.close (socket) < 0)
				UnixMarshal.ThrowExceptionForLastError ();
		}

		[Test]
		public void SocketPair ()
		{
			int socket1, socket2;
			if (Syscall.socketpair (UnixAddressFamily.AF_UNIX, UnixSocketType.SOCK_STREAM, 0, out socket1, out socket2) < 0)
				UnixMarshal.ThrowExceptionForLastError ();

			int r0 = Syscall.close (socket1);
			int r1 = Syscall.close (socket2);
			if (r0 < 0 || r1 < 0)
				UnixMarshal.ThrowExceptionForLastError ();
		}

		[Test]
		public void SendRecv ()
		{
			WithSocketPair ((so1, so2) => {
				long ret;
				var buffer1 = new byte[] { 42, 43, 44 };
				ret = Syscall.send (so1, buffer1, (ulong) buffer1.Length, 0);
				if (ret < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				var buffer2 = new byte[1024];
				ret = Syscall.recv (so2, buffer2, (ulong) buffer2.Length, 0);
				if (ret < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				Assert.AreEqual (buffer1.Length, ret);
				for (int i = 0; i < buffer1.Length; i++)
					Assert.AreEqual (buffer1[i], buffer2[i]);
			});
		}

		[Test]
		public void SockOpt ()
		{
			WithSockets (UnixAddressFamily.AF_UNIX, UnixSocketType.SOCK_STREAM, 0, (so1, so2) => {
				// Set SO_REUSEADDR to 1
				if (Syscall.setsockopt (so1, UnixSocketProtocol.SOL_SOCKET, UnixSocketOptionName.SO_REUSEADDR, 1) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				// Get and check SO_REUSEADDR
				int value;
				if (Syscall.getsockopt (so1, UnixSocketProtocol.SOL_SOCKET, UnixSocketOptionName.SO_REUSEADDR, out value) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (value, 1);

				// Set SO_REUSEADDR to 0
				if (Syscall.setsockopt (so1, UnixSocketProtocol.SOL_SOCKET, UnixSocketOptionName.SO_REUSEADDR, new byte[10], 4) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				// Get and check SO_REUSEADDR
				var buffer = new byte[15];
				long size = 12;
				if (Syscall.getsockopt (so1, UnixSocketProtocol.SOL_SOCKET, UnixSocketOptionName.SO_REUSEADDR, buffer, ref size) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (size, 4);
				for (int i = 0; i < size; i++)
					Assert.AreEqual (buffer[i], 0);
			});
		}

		[Test]
		public void SockOptLinger ()
		{
			WithSockets (UnixAddressFamily.AF_INET, UnixSocketType.SOCK_STREAM, UnixSocketProtocol.IPPROTO_TCP, (so1, so2) => {
				Linger linger = new Linger {
					l_onoff = 1,
					l_linger = 42,
				};
				// Set SO_LINGER
				if (Syscall.setsockopt (so1, UnixSocketProtocol.SOL_SOCKET, UnixSocketOptionName.SO_LINGER, linger) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				// Get and check SO_LINGER
				Linger value;
				if (Syscall.getsockopt (so1, UnixSocketProtocol.SOL_SOCKET, UnixSocketOptionName.SO_LINGER, out value) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (linger, value);
			});
		}

		[Test]
		public void Shutdown ()
		{
			WithSocketPair ((so1, so2) => {
				if (Syscall.shutdown (so1, ShutdownOption.SHUT_WR) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				var buffer2 = new byte[1024];
				long ret = Syscall.recv (so2, buffer2, (ulong) buffer2.Length, 0);
				if (ret < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				Assert.AreEqual (ret, 0);
			});
		}

		[Test]
		public unsafe void ByteOrder ()
		{
			ushort val1 = Syscall.htons (0x1234);
			byte* ptr1 = (byte*) &val1;
			Assert.AreEqual (ptr1[0], 0x12);
			Assert.AreEqual (ptr1[1], 0x34);

			uint val2 = Syscall.htonl (0x6789abcd);
			byte* ptr2 = (byte*) &val2;
			Assert.AreEqual (ptr2[0], 0x67);
			Assert.AreEqual (ptr2[1], 0x89);
			Assert.AreEqual (ptr2[2], 0xab);
			Assert.AreEqual (ptr2[3], 0xcd);

			ptr1[0] = 0xfe;
			ptr1[1] = 0xdc;
			Assert.AreEqual (Syscall.ntohs (val1), 0xfedc);

			ptr2[0] = 0x76;
			ptr2[1] = 0x54;
			ptr2[2] = 0x32;
			ptr2[3] = 0x10;
			Assert.AreEqual (Syscall.ntohl (val2), 0x76543210);
		}

		[Test]
		public void InAddrInAddr6 ()
		{
			var ip = IPAddress.Loopback;
			var inAddr = NativeConvert.ToInAddr (ip);
			Assert.AreEqual (ip, NativeConvert.ToIPAddress (inAddr));
			Assert.AreEqual (0x7f000001, Syscall.ntohl (inAddr.s_addr));

			var ip6 = IPAddress.IPv6Loopback;
			var in6Addr = NativeConvert.ToIn6Addr (ip6);
			Assert.AreEqual (ip6, NativeConvert.ToIPAddress (in6Addr));
			Assert.AreEqual (1, in6Addr[15]);

			Assert.AreEqual ("127.0.0.1", inAddr.ToString ());
			Assert.AreEqual ("::1", in6Addr.ToString ());
		}

		[Test]
		public void SockaddrUnTest ()
		{
			var address1 = new SockaddrUn ("/tmp/foo");
			Assert.AreEqual (address1.Path, "/tmp/foo");
			Assert.IsFalse (address1.IsAbstract);

			var storage = address1.ToSockaddrStorage ();
			var address2 = SockaddrUn.FromSockaddrStorage (storage);
			Assert.AreEqual (address1, address2);

			var sockaddr = Sockaddr.FromSockaddrStorage (storage);
			Assert.AreEqual (sockaddr.sa_family, address1.sa_family);

			var address3 = new SockaddrUn ("/tmp/bar", true);
			Assert.AreEqual (address3.Path, "/tmp/bar");
			Assert.IsTrue (address3.IsAbstract);

			Assert.AreEqual ("{sa_family=AF_UNIX, sun_path=\"/tmp/foo\"}", address1.ToString ());
			Assert.AreEqual ("{sa_family=AF_UNIX, sun_path=\"\\0/tmp/bar\"}", address3.ToString ());
		}

		[Test]
		public void SockaddrInTest ()
		{
			var address1 = new SockaddrIn {
				sin_family = UnixAddressFamily.AF_INET,
				sin_port = Syscall.htons (5678),
				sin_addr = NativeConvert.ToInAddr (IPAddress.Loopback),
			};

			var storage = address1.ToSockaddrStorage ();
			var address2 = SockaddrIn.FromSockaddrStorage (storage);
			Assert.AreEqual (address1, address2);

			var sockaddr = Sockaddr.FromSockaddrStorage (storage);
			Assert.AreEqual (sockaddr.sa_family, address1.sa_family);

			var storage2 = storage.ToSockaddrStorage ();
			Assert.AreEqual (storage, storage2);

			var storage3 = new SockaddrStorage (123);
			storage2.CopyTo (storage3);
			Assert.AreEqual (storage, storage3);

			Assert.AreEqual ("{sin_family=AF_INET, sin_port=htons(5678), sin_addr=127.0.0.1}", address1.ToString ());
		}

		[Test]
		public void SockaddrIn6Test ()
		{
			var address1 = new SockaddrIn6 {
				sin6_family = UnixAddressFamily.AF_INET6,
				sin6_port = Syscall.htons (1234),
				sin6_flowinfo = 2,
				sin6_addr = NativeConvert.ToIn6Addr (IPAddress.IPv6Loopback),
				sin6_scope_id = 3
			};

			var storage = address1.ToSockaddrStorage ();
			var address2 = SockaddrIn6.FromSockaddrStorage (storage);
			Assert.AreEqual (address1, address2);

			var sockaddr = Sockaddr.FromSockaddrStorage (storage);
			Assert.AreEqual (sockaddr.sa_family, address1.sa_family);

			Assert.AreEqual ("{sin6_family=AF_INET6, sin6_port=htons(1234), sin6_flowinfo=2, sin6_addr=::1, sin6_scope_id=3}", address1.ToString ());
		}

		[Test]
		public void BindConnect ()
		{
			WithSockets (UnixAddressFamily.AF_INET, UnixSocketType.SOCK_DGRAM, UnixSocketProtocol.IPPROTO_UDP, (so1, so2) => {
				// Bind UDP socket so1 to 127.0.0.1 with dynamic port
				var address = new SockaddrIn {
					sin_family = UnixAddressFamily.AF_INET,
					sin_port = Syscall.htons (0),
					sin_addr = new InAddr (127, 0, 0, 1),
				};
				if (Syscall.bind (so1, address) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				// Get actual port number using getsockname()
				var actualAddress = new SockaddrIn ();
				if (Syscall.getsockname (so1, actualAddress) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (actualAddress.sa_family, UnixAddressFamily.AF_INET);
				var port = Syscall.ntohs (actualAddress.sin_port);
				Assert.IsTrue (port != 0);


				// Connect so2 to so1
				var remoteAddress = new SockaddrIn {
					sin_family = UnixAddressFamily.AF_INET,
					sin_port = Syscall.htons (port),
					sin_addr = new InAddr (127, 0, 0, 1),
				};
				if (Syscall.connect (so2, remoteAddress) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				// Verify peer address using getpeername()
				var address2 = new SockaddrIn ();
				if (Syscall.getpeername (so2, address2) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (address2.sa_family, UnixAddressFamily.AF_INET);
				Assert.AreEqual (remoteAddress.sin_port, address2.sin_port);
				Assert.AreEqual (remoteAddress.sin_addr, address2.sin_addr);

				// Send and receive a few bytes
				long ret;
				var buffer1 = new byte[] { 42, 43, 44 };
				ret = Syscall.send (so2, buffer1, (ulong) buffer1.Length, 0);
				if (ret < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				var buffer2 = new byte[1024];
				ret = Syscall.recv (so1, buffer2, (ulong) buffer2.Length, 0);
				if (ret < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				Assert.AreEqual (buffer1.Length, ret);
				for (int i = 0; i < buffer1.Length; i++)
					Assert.AreEqual (buffer1[i], buffer2[i]);
			});
		}

		[Test]
		public void IPv6 ()
		{
			var address = new SockaddrIn6 {
				sin6_family = UnixAddressFamily.AF_INET6,
				sin6_port = Syscall.htons (0),
				sin6_addr = NativeConvert.ToIn6Addr (IPAddress.IPv6Loopback),
			};
			WithSockets (UnixAddressFamily.AF_INET6, UnixSocketType.SOCK_STREAM, 0, (so1, so2) => {
				if (Syscall.bind (so1, address) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				var address1Stor = new SockaddrStorage ();
				if (Syscall.getsockname (so1, address1Stor) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				var address1 = new SockaddrIn6 ();
				address1Stor.CopyTo (address1);

				// Check getsockname(socket, null)
				if (Syscall.getsockname (so1, null) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				var address2 = new SockaddrIn6 ();
				if (Syscall.getsockname (so1, address2) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				Assert.AreEqual (address1, address2);
				Assert.IsTrue (Syscall.ntohs (address1.sin6_port) != 0);
				address1.sin6_port = 0;
				Assert.AreEqual (address, address1);

				var address3 = new Sockaddr ();
				if (Syscall.getsockname (so1, address3) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (address.sa_family, address3.sa_family);

				// Try to store a sockaddr_in6 into a Sockaddr. Should fail because sockaddr_in6 should be larger than sockaddr_in
				var address4 = new SockaddrIn ();
				if (Syscall.getsockname (so1, address4) == 0)
					Assert.Fail ("getsockname() should have failed");
				Assert.AreEqual (Errno.ENOBUFS, Stdlib.GetLastError ());
			});
		}

		[Test]
		public void UnixAccept ()
		{
			var address = new SockaddrUn (TempFolder + "/socket1");
			var address2 = SockaddrUn.FromSockaddrStorage (address.ToSockaddrStorage ());
			Assert.AreEqual (address, address2);

			WithSockets (UnixAddressFamily.AF_UNIX, UnixSocketType.SOCK_STREAM, 0, (so1, so2) => {
				if (Syscall.bind (so1, address) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				if (Syscall.listen (so1, 5) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				if (Syscall.connect (so2, address) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				var address3 = new SockaddrUn ();
				if (Syscall.getsockname (so1, address3) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (address, address3);

				var address4 = new SockaddrStorage ();
				if (Syscall.getsockname (so1, address4) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (UnixAddressFamily.AF_UNIX, address4.sa_family);
				Assert.AreEqual (address3, SockaddrUn.FromSockaddrStorage (address4));

				var address5 = new SockaddrUn ();
				if (Syscall.getsockname (so1, address5) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (UnixAddressFamily.AF_UNIX, address5.sa_family);

				// Check getsockname(socket, null)
				if (Syscall.getsockname (so1, null) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				int so3;
				var remote = new SockaddrUn ();
				if ((so3 = Syscall.accept (so1, remote)) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				try {
					// Send and receive a few bytes
					long ret;
					var buffer1 = new byte[] { 42, 43, 44 };
					ret = Syscall.send (so2, buffer1, (ulong) buffer1.Length, 0);
					if (ret < 0)
						UnixMarshal.ThrowExceptionForLastError ();

					var buffer2 = new byte[1024];
					ret = Syscall.recv (so3, buffer2, (ulong) buffer2.Length, 0);
					if (ret < 0)
						UnixMarshal.ThrowExceptionForLastError ();

					Assert.AreEqual (buffer1.Length, ret);
					for (int i = 0; i < buffer1.Length; i++)
						Assert.AreEqual (buffer1[i], buffer2[i]);
				} finally {
					if (Syscall.close (so3) < 0)
						UnixMarshal.ThrowExceptionForLastError ();
				}
			});
		}

		[Test]
		public void Accept4 ()
		{
			WithSockets (UnixAddressFamily.AF_UNIX, UnixSocketType.SOCK_STREAM, 0, (so1, so2) => {
				var address = new SockaddrUn (TempFolder + "/socket2");
				if (Syscall.bind (so1, address) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				if (Syscall.listen (so1, 5) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				if (Syscall.connect (so2, address) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				int so3;
				var remote = new SockaddrUn ();
				if ((so3 = Syscall.accept4 (so1, remote, UnixSocketFlags.SOCK_CLOEXEC | UnixSocketFlags.SOCK_NONBLOCK)) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				try {
					int _flags;
					if ((_flags = Syscall.fcntl (so3, FcntlCommand.F_GETFL)) < 0)
						UnixMarshal.ThrowExceptionForLastError ();
					var flags = NativeConvert.ToOpenFlags (_flags);
					Assert.IsTrue ((flags & OpenFlags.O_NONBLOCK) != 0);

					int _flagsFD;
					if ((_flagsFD = Syscall.fcntl (so3, FcntlCommand.F_GETFD)) < 0)
						UnixMarshal.ThrowExceptionForLastError ();
					// FD_CLOEXEC must be set
					//var flagsFD = NativeConvert.ToFdFlags (_flagsFD);
					//Assert.IsTrue ((flagsFD & FdFlags.FD_CLOEXEC) != 0);
					Assert.IsTrue (_flagsFD != 0);
				} finally {
					if (Syscall.close (so3) < 0)
						UnixMarshal.ThrowExceptionForLastError ();
				}
			});
		}

		[Test]
		public void SendToRecvFrom ()
		{
			WithSockets (UnixAddressFamily.AF_INET, UnixSocketType.SOCK_DGRAM, UnixSocketProtocol.IPPROTO_UDP, (so1, so2) => {
				// Bind UDP socket so1 to 127.0.0.1 with dynamic port
				var address = new SockaddrIn { sin_family = UnixAddressFamily.AF_INET, sin_port = Syscall.htons (0), sin_addr = new InAddr (127, 0, 0, 1) };
				if (Syscall.bind (so1, address) < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				// Get actual port number using getsockname()
				var actualAddress = new SockaddrIn ();
				if (Syscall.getsockname (so1, actualAddress) < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (actualAddress.sa_family, UnixAddressFamily.AF_INET);
				var port = Syscall.ntohs (actualAddress.sin_port);
				Assert.IsTrue (port != 0);


				var remoteAddress = new SockaddrIn {
					sin_family = UnixAddressFamily.AF_INET,
					sin_port = Syscall.htons (port),
					sin_addr = new InAddr (127, 0, 0, 1),
				};

				// Send and receive a few bytes
				long ret;
				var buffer1 = new byte[] { 42, 43, 44 };
				ret = Syscall.sendto (so2, buffer1, (ulong) buffer1.Length, 0, remoteAddress);
				if (ret < 0)
					UnixMarshal.ThrowExceptionForLastError ();

				var senderAddress = new SockaddrIn ();
				var buffer2 = new byte[1024];
				ret = Syscall.recvfrom (so1, buffer2, (ulong) buffer2.Length, 0, senderAddress);
				if (ret < 0)
					UnixMarshal.ThrowExceptionForLastError ();
				Assert.AreEqual (senderAddress.sa_family, UnixAddressFamily.AF_INET);
				Assert.AreEqual (senderAddress.sin_addr, new InAddr (127, 0, 0, 1));

				Assert.AreEqual (buffer1.Length, ret);
				for (int i = 0; i < buffer1.Length; i++)
					Assert.AreEqual (buffer1[i], buffer2[i]);
			});
		}
	}
}

// vim: noexpandtab
// Local Variables: 
// tab-width: 4
// c-basic-offset: 4
// indent-tabs-mode: t
// End: 

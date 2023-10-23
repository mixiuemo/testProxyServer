using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using StreamThreadPool;
using System.Configuration;
using VideoMonitor;

namespace VideoMonitor
{
    public class ProxyServer
    {
        private int ServerPort;
        private Socket serverSocket;
        private String _name;
        private List<SocketPair> pairs = new List<SocketPair>();
        public static Dictionary<String, String> CameraRequestMap = new Dictionary<string, string>();
        public static Dictionary<String, String> CameraCloseMap = new Dictionary<string, String>();
        public static Dictionary<String, SocketPair> pairsMap = new Dictionary<String, SocketPair>();

        public ProxyServer(String name)
        {
            _name = name;
        }

        public int Start(int sPort)
        {
            int lPort = GetRandomUnusedPort();
            return Start(lPort,sPort);
        }

        public void Stop()
        {
            try
            {
                serverSocket.Close();
                serverSocket.Dispose();
                serverSocket = null;
            }
            catch (Exception ex) {
                serverSocket.Dispose();
                serverSocket = null;
            }
        }
        public int Start(int lPort,int sPort)
        {
            ServerPort = sPort;
            IPEndPoint iep = new IPEndPoint(IPAddress.Any, lPort);
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            serverSocket.Bind(iep);
            serverSocket.Listen(50);    //设定最多20个排队连接请求  
            int port = ((IPEndPoint)serverSocket.LocalEndPoint).Port;
            Thread myThread = new Thread(ListenClientConnect);
            myThread.Start();
            return port;
        }
        private int GetRandomUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void remove(SocketPair pair)
        {
            pairs.Remove(pair);
        }

        private void ListenClientConnect()
        {
            while (true)
            {
                Socket clientSocket = serverSocket.Accept();
                if (CheckIP(clientSocket) == false)
                {
                    clientSocket.Close();
                    break;
                }
                Socket toServerSocket = null;
                try
                {
                    toServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    String clientIp = ((IPEndPoint)clientSocket.RemoteEndPoint).Address.ToString();
                    if (!CameraRequestMap.ContainsKey(clientIp) && !CameraCloseMap.ContainsKey(clientIp))
                    {
                        //clientSocket.Close();
                        continue;
                    }
                    String ip="";
                    if (CameraCloseMap.ContainsKey(clientIp))
                    {
                        ip=CameraCloseMap[clientIp];
                        Console.WriteLine("close ip=" + ip);
                        CameraCloseMap.Remove(clientIp);
                    }
                    else
                        ip = CameraRequestMap[clientIp];

                    if (ip.Equals(""))
                        continue;

                    String provider = clientIp+ip;
                    IPAddress ipAddress = IPAddress.Parse(ip);
                    //IPAddress ipAddress = IPAddress.Loopback;
                    IPEndPoint remoteEndPoint = new IPEndPoint(ipAddress, ServerPort);
                    toServerSocket.Connect(remoteEndPoint);
                    //Console.WriteLine("serverPort=" + ServerPort+",ip="+ip);

                    if (toServerSocket.Connected)
                    {
                        if (ServerPort == 554)
                        {
                            if (pairsMap.ContainsKey(provider))
                            {
                                SocketPair sp = pairsMap[provider];
                                if (pairs.Contains(sp))
                                    pairs.Remove(sp);
                            }
                            
                            CameraRequestMap[clientIp] = "";
                            //CameraRequestMap.Remove(clientIp);
                        }
                        SocketPair pair = new SocketPair(this, toServerSocket, clientSocket, ServerPort.ToString());
                        pairs.Add(pair);
                        pairsMap[provider] = pair;
                    }
                }
                catch(Exception ex)
                {
                    //String exStr = ex.ToString();
                    //if (exStr.Contains(":8000"))
                    //{
                    //    String str = "请求连接摄像头失败！";
                    //    VideoManager.addHint(str);
                    //}
                    //VideoManager.logger.Warming("pair error=", ex);
                    if(clientSocket.Connected)
                        clientSocket.Close();
                }
            }
        }

        private bool CheckIP(Socket myClientSocket)
        {
            String clientIp = ((IPEndPoint)myClientSocket.RemoteEndPoint).Address.ToString();
            return CheckIP(clientIp);
        }

        public static bool CheckIP(String clientIp)
        {
            try
            {
                if (clientIp.Equals("127.0.0.1"))
                    return true;
                if (clientIp.IndexOf("21.7.10.") >= 0)
                    return true;
                String iniPath = VideoManager.iniFilePath;
                IniFiles ini = new IniFiles(iniPath);
                String allowIpStr = ini.ReadString("VIDEO", "AllowIp", "").Trim();
                if (allowIpStr.Equals(""))
                    return false;
                String[] ips = allowIpStr.Split(new Char[] { ';' });
                if (isMyIP(clientIp))
                    return true;
                for (int i = 0; i < ips.Length; i++)
                {
                    int flag = ips[i].IndexOf("-");
                    if (flag > 0)
                    {
                        string[] segmentStr = ips[i].Split(new char[] { '-' });
                        string[] beginArr = segmentStr[0].Split(new char[] { '.' });
                        string[] endArr = segmentStr[1].Split(new char[] { '.' });
                        int begin = int.Parse(beginArr[3]);
                        int end = int.Parse(endArr[3]);
                        string startStr = beginArr[0] + "." + beginArr[1] + "." + beginArr[2];
                        for (; begin <= end; begin++)
                        {
                            string tmp = startStr + "." + begin;
                            if (clientIp.Equals(tmp))
                                return true;
                        }
                    }
                    else
                    {
                        if (clientIp.Equals(ips[i].Trim()))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool isMyIP(string ip)
        {
            try
            {
                string hostInfo = Dns.GetHostName();
                //IP地址  
                System.Net.IPAddress[] addressList = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
                for (int i = 0; i < addressList.Length; i++)
                {
                    if (ip.Equals(addressList[i].ToString()))
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            { }

            if (ip.Equals("127.0.0.1") || ip.Equals("localhost"))
                return true;
            return false;
        }
    }

    public class SocketPair
    {
        byte[] ClientBuffer = new byte[1024];
        byte[] ServerBuffer = new byte[1024];
        public Socket serverSocket;
        public Socket clientSocket;
        private ProxyServer server;
        private string name;
        private AsyncCallback asyncCallback;
        private AsyncCallback clientAsyncCallback;
        private AsyncCallback sendToServerAsyncCallback;
        private AsyncCallback sendToClientAsyncCallback;
        public SocketPair(ProxyServer serv,Socket s1, Socket s2,string name)
        {
            this.name = name;
            server = serv;
            serverSocket = s1;
            clientSocket = s2;
            
            asyncCallback = new AsyncCallback(this.OnReceiveServerBytes);
            clientAsyncCallback = new AsyncCallback(this.OnReceiveClientBytes);
            sendToServerAsyncCallback = new AsyncCallback(SendToServerCallback);
            sendToClientAsyncCallback = new AsyncCallback(SendToClientCallback);
            clientSocket.BeginReceive(ClientBuffer, 0, ClientBuffer.Length, SocketFlags.None, clientAsyncCallback, clientSocket);
            serverSocket.BeginReceive(ServerBuffer, 0, ServerBuffer.Length, SocketFlags.None, asyncCallback, serverSocket);
        }

        protected void OnReceiveClientBytes(IAsyncResult ar)
        {
            try
            {
                int len = clientSocket.EndReceive(ar);
                if (len <= 0)
                {
                    clientSocket.BeginReceive(ClientBuffer, 0, ClientBuffer.Length, SocketFlags.None, clientAsyncCallback, clientSocket);
                    return;
                }
                //byte[] sendBuf = new byte[len];
                //System.Buffer.BlockCopy(ClientBuffer, 0, sendBuf, 0, len);
                SendToServer(ClientBuffer, len);
                clientSocket.BeginReceive(ClientBuffer, 0, ClientBuffer.Length, SocketFlags.None, clientAsyncCallback, clientSocket);
            }
            catch(Exception ex)
            {
                Dispose(false);
            }
        }

        protected void SendToServer(byte[] Packet,int len)
        {
            try
            {
                //serverSocket.BeginSend(Packet, 0, len, SocketFlags.None, sendToServerAsyncCallback, serverSocket);
                serverSocket.Send(Packet, 0, len, SocketFlags.None);
            }
            catch (Exception e)
            {
                Dispose(false);
            }
        }

        private void SendToServerCallback(IAsyncResult asyncResult)
        {
            try
            {
                serverSocket.EndSend(asyncResult);
            }
            catch (Exception e)
            {
                Dispose(false);
            }
        }

        protected void OnReceiveServerBytes(IAsyncResult ar)
        {
            try
            {
                int len = serverSocket.EndReceive(ar);
                if (len <= 0)
                {
                    serverSocket.BeginReceive(ServerBuffer, 0, ServerBuffer.Length, SocketFlags.None, asyncCallback, serverSocket);
                    return;
                }
                //byte[] sendBuf = new byte[len];
                //System.Buffer.BlockCopy(ServerBuffer, 0, sendBuf, 0, len);
                SendToClient(ServerBuffer,len);
                serverSocket.BeginReceive(ServerBuffer, 0, ServerBuffer.Length, SocketFlags.None, asyncCallback, serverSocket);
                //Array.Clear(sendBuf, 0, len);
            }
            catch (Exception ex)
            {
                 Dispose(false);
            }
        }

        protected void SendToClient(byte[] Packet,int len)
        {
            try
            {
                if (clientSocket == null)
                {
                    server.remove(this);
                    return;
                }
                //clientSocket.BeginSend(Packet, 0, len, SocketFlags.None, sendToClientAsyncCallback, clientSocket);
                clientSocket.Send(Packet, 0, len, SocketFlags.None);

            }
            catch(Exception e)
            {
                Dispose(false);
            }
        }

        private void SendToClientCallback(IAsyncResult asyncResult)
        {
            try
            {
                if (clientSocket == null)
                {
                    serverSocket = null;
                    server.remove(this);
                    return;
                }
                clientSocket.EndSend(asyncResult);
            }
            catch (Exception e)
            {
                 Dispose(false);
            }
        }

        protected void Dispose(bool val)
        {
            try
            {
                if (clientSocket == null || serverSocket == null)
                {
                    //server.remove(this);
                }
                else
                {
                    if (clientSocket.Connected)
                    {
                        clientSocket.Close();
                        clientSocket.Dispose();
                    }
                    if (serverSocket.Connected)
                    {
                        serverSocket.Close();
                        serverSocket.Dispose();
                    }
                    clientSocket = null;
                    serverSocket = null;
                }
                server.remove(this);
            }
            catch { }
        }
    }

}

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace paint
{
    internal class Server
    {
        static UdpClient udpConnServer = new UdpClient(13131);
        static UdpClient udpPaintServer = new UdpClient(21377);
        static List<IPEndPoint> udpClients = new List<IPEndPoint>();
        static BlockingCollection<KeyValuePair<byte, byte[]>> paintData = new BlockingCollection<KeyValuePair<byte, byte[]>>(new ConcurrentQueue<KeyValuePair<byte, byte[]>>(), 20000);

        static void Main(string[] args)
        {
            Thread connectionThread = new Thread(ListenForConnections);
            connectionThread.Start();

            Thread receivingThread = new Thread(ReceiveDrawingData);
            receivingThread.Start();

            Thread sendingThread = new Thread(SendDrawingData);
            sendingThread.Start();

            Console.WriteLine("Server started on port {0}", ((IPEndPoint)udpConnServer.Client.LocalEndPoint).Port);
        }

        static void ListenForConnections()
        {
            try
            {
                while (true)
                {
                    // Najprostszy kod na odbieranie - czekamy, aż coś dostaniemy z dowolnego adresu IP
                    // Po odebraniu `ep` zostanie zaktualizowane danymi adresowymi klienta
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedBytes = udpConnServer.Receive(ref ep);

                    // Konwersja datagramu na string
                    string msg = Encoding.ASCII.GetString(receivedBytes);
                    Console.WriteLine("> {0}", msg);
                    if (msg == "connect")
                    {
                        Console.WriteLine("{0} connected\n", ep);
                        // Dodajemy klienta do listy i przesyłamy mu port serwera odbiorczego danych rysunkowych.
                        // Jego identyfikatorem staje się indeks jego adresu
                        udpClients.Add(ep);
                        byte[] m = BitConverter.GetBytes((short)((IPEndPoint)udpPaintServer.Client.LocalEndPoint).Port);
                        udpConnServer.Send(m, m.Length, ep);
                    }
                    else if (msg == "disconnect")
                    {
                        // Usuwanie klienta z listy
                        udpClients.Remove(udpClients.Find(x => x.Equals(ep)));
                        Console.WriteLine("{0} disconnected\n", ep);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }

        static void ReceiveDrawingData()
        {
            try
            {
                while (true)
                {
                    // Znowu, czekamy na dowolne dane, następnie sprawdzamy, czy adres tego klienta
                    // znajduje się na liście. Jeśli nie, to nasłuchujemy od nowa.
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedBytes = udpPaintServer.Receive(ref remoteEndPoint);
                    var client = udpClients.FindIndex(x => x.Equals(remoteEndPoint));
                    if (client == -1)
                    {
                        Console.WriteLine("Unknown client {0}", remoteEndPoint);
                        continue;
                    }
                   
                    if (Debugger.IsAttached)
                    {
                        // Pierwszy bajt jest bajtem informującym nas o rodzaju przesyłanych danych
                        // 1 - Wybrany kolor
                        // 2 - Nowy punkt na obrazie
                        switch (receivedBytes[0])
                        {
                            case 0x01: // Color
                                {
                                    Console.WriteLine("{0} has started drawing", remoteEndPoint);
                                    break;
                                }
                            case 0x02:
                                {
                                    Console.WriteLine("{0} sent point", remoteEndPoint);
                                    byte[] receivedBytes2 = new byte[receivedBytes.Length - 1];
                                    Array.Copy(receivedBytes, 1, receivedBytes2, 0, receivedBytes.Length - 1);
                                    Message message = Message.Deserialize(receivedBytes2);
                                    Console.WriteLine("Received message from client: X = " + message.X + ", Y = " + message.Y);
                                    break;
                                }
                            default:
                                continue;
                        }
                    }
                    // Dodawanie danych rysunkowych do blokującej kolejki
                    // ID klienta to jego indeks na liście klientów
                    paintData.Add(new KeyValuePair<byte, byte[]>((byte)udpClients.FindIndex(x => x.Equals(remoteEndPoint)), receivedBytes));
                }
            }

            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }

        static void SendDrawingData()
        {
            try
            {
                while (true)
                {
                    // Wybieramy dane rysunkowe, dopinamy id klienta,
                    // który je wysłał i przesyłamy do wszystkich naszych klientów.
                    var data = paintData.Take();
                    byte[] toSendBytes = new byte[1 + data.Value.Length];
                    toSendBytes[0] = data.Key;
                    Buffer.BlockCopy(data.Value, 0, toSendBytes, 1, data.Value.Length);

                    foreach (var client in udpClients)
                    {
                        udpPaintServer.Send(toSendBytes, toSendBytes.Length, client);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }
    }
}

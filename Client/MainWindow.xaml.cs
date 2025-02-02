using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using static System.Windows.Forms.LinkLabel;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Net;
using System.Net.Sockets;
using System.Data;
using System.Windows.Forms;
using System.Drawing;


namespace paint
{
    public partial class MainWindow : System.Windows.Window
    {
        System.Windows.Media.Brush brush = System.Windows.Media.Brushes.Black;
        private System.Windows.Point? previousPoint = null;

        //server
        UdpClient udpClient = new UdpClient();
        bool connected;
        IPEndPoint udpEndPoint;
        Task receiveTask;
        System.Drawing.Color myColor = System.Drawing.Color.Black;
        Dictionary<byte, System.Drawing.Color> udpClients;


        public MainWindow()
        {
            InitializeComponent();
            Disconnect.IsEnabled = false;
            udpClients = new Dictionary<byte, System.Drawing.Color>();
        }

        private void UpdateColorCanvas()
        {
            colorCanvas.Background = brush;
        }

        private void onColorPickerClick(object sender, RoutedEventArgs e)
        {
            ColorDialog colorDialog = new ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                this.brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                colorDialog.Color.R,
                colorDialog.Color.G,
                colorDialog.Color.B));

                myColor = colorDialog.Color;
                UpdateColorCanvas();
            }
        }


        private void canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!connected) return;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                System.Windows.Point currentPoint = e.GetPosition(canvas);

                if (previousPoint != null)
                {
                    double distance = DistanceBetweenPoints(previousPoint.Value, currentPoint);
                    int steps = (int)Math.Ceiling(distance / 5);
                    double deltaX = (currentPoint.X - previousPoint.Value.X) / steps;
                    double deltaY = (currentPoint.Y - previousPoint.Value.Y) / steps;

                    for (int i = 0; i < steps; i++)
                    {
                        Message message = new Message { X = previousPoint.Value.X + i * deltaX, Y = previousPoint.Value.Y + i * deltaY };
                        byte[] messageSerialized = message.Serialize();

                        byte[] data = new byte[16 + 1];
                        data[0] = 0x02;
                        Buffer.BlockCopy(messageSerialized, 0, data, 1, messageSerialized.Length);
                        udpClient.Send(data, data.Length);
                    }
                }
                previousPoint = currentPoint;
            }
            else
            {
                previousPoint = null;
            }
        }


        private void canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (connected == false)
                return;

            //Send color to server
            byte[] data = new byte[5];
            data[0] = 0x01;
            Buffer.BlockCopy(BitConverter.GetBytes(myColor.ToArgb()), 0, data, 1, sizeof(int));
            udpClient.Send(data, data.Length);
        }

        private double DistanceBetweenPoints(System.Windows.Point p1, System.Windows.Point p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }


        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (connected == false)
            {
                udpClient.Connect(IP.Text, int.Parse(Port.Text));
                udpClient.Send(Encoding.ASCII.GetBytes("connect"), 7);

                udpEndPoint = new IPEndPoint(IPAddress.Any, 0);
                var data = udpClient.Receive(ref udpEndPoint);
                // Odczytujemy port, na którym serwer danych rysunkowych będzie nas słuchał
                udpEndPoint.Port = BitConverter.ToInt16(data, 0);

                // Teraz bindujemy się do serwera danych rysunkowych i wywołujemy wątek odbiorczy
                udpClient.Connect(udpEndPoint);

                connected = true;
                receiveTask = Task.Run(new Action(ReceiveMessages));
                ConnectionStatus.Text = "Connected";

                Disconnect.IsEnabled = true;
                Connect.IsEnabled = false;
            }
        }

        private void ReceiveMessages()
        {
            try
            {
                while (connected)
                {
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedBytes = udpClient.Receive(ref remoteEndPoint);

                    byte id = receivedBytes[0];
                    switch (receivedBytes[1])
                    {
                        //color
                        case 0x01:
                            {
                                byte[] color = new byte[4];
                                Buffer.BlockCopy(receivedBytes, 2, color, 0, color.Length);
                                udpClients[id] = System.Drawing.Color.FromArgb(BitConverter.ToInt32(color, 0));
                                break;
                            }
                        //point
                        case 0x02:
                            {
                                byte[] messageBytes = new byte[receivedBytes.Length - 2];
                                Array.Copy(receivedBytes, 2, messageBytes, 0, receivedBytes.Length - 2);
                                Message receivedMessage = Message.Deserialize(messageBytes);

                                //Draw point on canvas
                                Dispatcher.Invoke(() =>
                                {
                                    System.Windows.Shapes.Rectangle rectangle = new System.Windows.Shapes.Rectangle();

                                    System.Drawing.Color color = udpClients[id];
                                    System.Windows.Media.Brush brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
                                    rectangle.Fill = brush;
                                    rectangle.Width = 5;
                                    rectangle.Height = 5;

                                    Canvas.SetLeft(rectangle, receivedMessage.X);
                                    Canvas.SetTop(rectangle, receivedMessage.Y);

                                    canvas.Children.Add(rectangle);
                                });
                                break;
                            }
                    }
                }
            }
            catch (SocketException e)
            {
                if (connected == true)
                    System.Windows.MessageBox.Show(e.Message + "\n" + e.StackTrace);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                System.Windows.Forms.MessageBox.Show(e.Message + "\n" + e.StackTrace);
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            if (connected == true)
            {
                udpClient.Connect(IP.Text, int.Parse(Port.Text));
                udpClient.Send(Encoding.ASCII.GetBytes("disconnect"), 10);
                udpClient.Close();

                // Czekamy na zakończenie wątku odbiorczego
                connected = false;
                receiveTask.Wait();

                udpClient = new UdpClient();
                ConnectionStatus.Text = "Disconnected";
                Disconnect.IsEnabled = false;
                Connect.IsEnabled = true;
            }
        }
    }
}
using System.Windows;
using System.Threading;
using System.Windows.Controls;
using System.IO.Ports;
using System;

namespace max30105_demo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            FindPorts();
        }

        private SerialPort serialPort = new SerialPort();

        public void ErrorMsg(string msg)
        {
            MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public void Information(string msg)
        {
            MessageBox.Show(msg, "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void FindPorts()
        {
            portsComboBox.ItemsSource = SerialPort.GetPortNames();
            if (portsComboBox.Items.Count > 0)
            {
                portsComboBox.SelectedIndex = 0;
                portsComboBox.IsEnabled = true;
            }
            else
            {
                portsComboBox.IsEnabled = false;
                ErrorMsg("No ports found");
                Application.Current.Shutdown();
            }
        }

        private bool OpenPort()
        {
            bool flag = false;
            ConfigurePort();

            try
            {
                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();
                Information(string.Format("Port {0} opened, Baud rate {1}", serialPort.PortName, serialPort.BaudRate.ToString()));
                flag = true;
            }
            catch (Exception ex)
            {
                ErrorMsg(ex.Message);
            }

            return flag;
        }

        private bool ClosePort()
        {
            bool flag = false;

            try
            {
                serialPort.Close();
                Information(string.Format("Port {0} closed", serialPort.PortName));
                flag = true;
            }
            catch (Exception ex)
            {
                ErrorMsg(ex.Message);
            }

            return flag;
        }

        private void ConfigurePort()
        {
            serialPort.PortName = GetSelectedPortName();
            serialPort.BaudRate = 115200;
        }

        private string GetSelectedPortName()
        {
            return portsComboBox.Text;
        }

        //private void ReadSerialPort(object sender, SerialDataReceivedEventArgs e)
        //{
        //    if (!(OpenPort()))
        //    {
        //        ErrorMsg("Port not opened");
        //        return;
        //    }
        //    heartRateVal.Text = "ready";

        //    //int bytesToRead = sp.BytesToRead;
        //    //byte[] buf = new byte[bytesToRead];

        //    //sp.Read(buf, 0, bytesToRead);


        //    // string recvData = buf.ToString();
        //    string recvData = serialPort.ReadExisting();
        //    heartRateVal.Text = "reading";
        //    heartRateVal.Text = recvData;
        //}


        private string ReadPort(SerialPort sPort)
        {
            string readStr="";

            if (!(sPort.IsOpen))
            {
                return readStr;
            }
            if (sPort.BytesToRead > 0)
            {
                readStr = sPort.ReadLine();
                //string heartRate = recvData.Split('=')[1];

                //Information(recvData);
            }
            return readStr;
        }

        private void RefreshStatus(string status)
        {
            string hRate = status.Split(',')[0].Split('=')[1];
            string spo2 = status.Split(',')[1].Split('=')[1];
            this.Dispatcher.Invoke(() =>
            {
                heartRateVal.Text = hRate;
                spo2Val.Text = spo2;
            });
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (button.Content.ToString() == "Start")
            {
                button.Content = "Stop";
                OpenPort();
                Thread read = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            RefreshStatus(ReadPort(serialPort));
                        } catch
                        {
                            //ErrorMsg(ex.Message);
                        }
                        Thread.Sleep(100);
                    }
                });
                read.Start();
                //heartRateVal.Text = ReadPort(serialPort);
            }
            else if (button.Content.ToString() == "Stop")
            {
                ClosePort();
                button.Content = "Start";
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}

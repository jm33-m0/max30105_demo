using System;
using System.ComponentModel;
using System.IO.Ports;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Diagnostics;

namespace max30105_demo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialPort serialPort = new SerialPort();
        private bool useTimer;
        private int timeout = 1;

        DispatcherTimer dispatcherTimer = new DispatcherTimer();
        public DispatcherTimer DispatcherTimer { get => dispatcherTimer; set => dispatcherTimer = value; }

        public MainWindow()
        {
            InitializeComponent();
            FindPorts();
            //timerCheck.IsChecked = true;
            Run();
        }

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

            if (!serialPort.IsOpen)
            {
                return true;
            }

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

        private string ReadPort(SerialPort sPort)
        {
            string readStr="";

            if (!sPort.IsOpen)
            {
                return readStr;
            }
            if (sPort.BytesToRead > 0)
            {
                readStr = sPort.ReadLine();
            }
            return readStr;
        }

        private void RefreshStatus(string status)
        {
            string hRate = status.Split(',')[0].Split('=')[1];
            string spo2 = status.Split(',')[1].Split('=')[1];
            this.Dispatcher.Invoke(() =>
            {
                heartRateVal.Text = hRate + "bpm";
                spo2Val.Text = spo2;
            });
        }


        public void Run()
        {
            button.Content = "Stop";
            statusText.Text = "Running...";

            if (useTimer)
            {
                // start timer
                TimerCtl("on");
            }

            OpenPort();
            Thread read = new Thread(() =>
            {
                while (true)
                {
                    if (!serialPort.IsOpen)
                    {
                        return;
                    }

                    try
                    {
                        RefreshStatus(ReadPort(serialPort));
                    } catch
                    {
                        //ErrorMsg(ex.Message);
                    }
                    Thread.Sleep(50);
                }
            });
            read.Start();
        }

        private void DispatcherTimer_Tick(object sender, EventArgs e)
        {
            Information("Time is up!");

            string locker = Environment.GetEnvironmentVariable("windir") + @"\System32\rundll32.exe";
            Process.Start(locker, "user32.dll,LockWorkStation");
        }

        private void TimerCtl(string switchAction)
        {
            switch (switchAction)
            {
                case "on":
                    dispatcherTimer.Tick += new EventHandler(DispatcherTimer_Tick);
                    dispatcherTimer.Interval = new TimeSpan(0, timeout, 0);
                    dispatcherTimer.Start();

                    statusText.Text = "Running, timer started...";
                    break;
                case "off":
                    dispatcherTimer.Stop();
                    statusText.Text = "Timer stopped...";
                    break;
            }
        }

        public void Stop()
        {
            if (useTimer)
            {
                // stop timer
                TimerCtl("off");
            }

            ClosePort();
            button.Content = "Start";
            statusText.Text = "Ready";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (button.Content.ToString() == "Start")
            {
                Run();
            }
            else if (button.Content.ToString() == "Stop")
            {
                Stop();
            }
        }

        public void Window_Closing(object sender, CancelEventArgs e)
        {
            string msg = "Quit application";
            MessageBoxResult result =
              MessageBox.Show(
                msg,
                "Are you sure?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                // If user doesn't want to close, cancel closure
                e.Cancel = true;
            }
            Stop();
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // nothing to do
        }

        private void TimerCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            useTimer = false;
            TimerCtl("off");
        }

        private void TimerCheck_Checked(object sender, RoutedEventArgs e)
        {
            useTimer = true;
            TimerCtl("on");
        }

        private void TimeoutVal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!Int32.TryParse(timeoutVal.Text, out timeout) ||
                timeoutVal.Text.Length > 3)
            {
                timeoutVal.Text = "1";
                timeout = 1;
            }
        }
    }
}
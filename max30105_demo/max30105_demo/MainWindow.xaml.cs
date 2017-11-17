using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace max30105_demo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialPort serialPort = new SerialPort();
        private bool useTimer;
        private int timeout = 120;
        private int hrCount, spo2Count;

        //public int hrLvlow, spo2Lvlow, hrLvHi, spo2LvHi;

        private Thread read;

        DispatcherTimer mainTimer = new DispatcherTimer();
        DispatcherTimer assistTimer = new DispatcherTimer();
        public DispatcherTimer Maintimer { get => mainTimer; set => mainTimer = value; }
        public DispatcherTimer AssistTimer { get => assistTimer; set => assistTimer = value; }

        public MainWindow()
        {
            InitializeComponent();

            if (CheckMinimize())
            {
                minimizeCheck.IsChecked = true;
            }

            fingerDection.IsChecked = true;
            FindPorts();
            Run();
        }

        public void ErrorMsg(string msg)
        {
            MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool Alert(string msg)
        {
            bool retVal = false;
            MessageBoxResult answ = MessageBox.Show(msg, "Alert", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answ == MessageBoxResult.Yes)
            {
                retVal = true;
            }
            return retVal;
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

            if (serialPort.IsOpen)
            {
                return true;
            }

            ConfigurePort();

            try
            {
                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();
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
                try
                {
                    readStr = sPort.ReadLine();
                } catch (Exception ex)
                {
                    ErrorMsg("ReadPort: " + ex.Message);
                }
            }

            if (!readStr.Contains("HR="))
            {
                return "";
            }

            return readStr;
        }

        private void Check_HRSPO2(string hr, string spo2)
        {
            int hrVal = Int32.Parse(hr);
            int spo2Val = Int32.Parse(spo2);

            if (hrVal >= 120 || hrVal <= 72)
            {
                Outbox.Text += "Abnormal HR: " + hr + "\n";
                hrCount++;
            }

            if (spo2Val > 95 || spo2Val <= 90)
            {
                Outbox.Text += "Abnormal SPO2: " + spo2 + "\n";
                spo2Count++;
            }
            Outbox.Focus();
            Outbox.CaretIndex = Outbox.Text.Length;
            Outbox.ScrollToEnd();

            if (spo2Count > 30)
            {
                if (Alert("Check your SPO2 data"))
                {
                    Visibility = Visibility.Visible;
                    WindowState = WindowState.Normal;
                }
                spo2Count = 0;
            } else if (hrCount > 30)
            {
                if (Alert("Check your heartrate data"))
                {
                    Visibility = Visibility.Visible;
                    WindowState = WindowState.Normal;
                }
                hrCount = 0;
            }
        }

        private bool RefreshStatus(string status)
        {
            // can also be used to detect finger presence
            bool flag = false;
            bool fingerDectionChecked = false;

            if (status == "")
            {
                return flag;
            }


            this.Dispatcher.Invoke(() =>
            {
                fingerDectionChecked = fingerDection.IsChecked == true;
            });

            string hRate, spo2;
            try
            {
                hRate = status.Split(',')[0].Split('=')[1];
                spo2 = status.Split(',')[1].Split('=')[1];
                flag = true;
            } catch
            {
                return flag;
            }

            if (fingerDectionChecked)
            {
                if (status.Contains("HR=0"))
                {
                    //return flag;
                    flag = false;

                    this.Dispatcher.Invoke(() =>
                    {
                        statusText.Text = "No finger";

                        if (!assistTimer.IsEnabled)
                        {
                            assistTimer.Interval= new TimeSpan(0, 0, 5);
                            assistTimer.Tick += new EventHandler(AssistTimer_Tick);
                            assistTimer.Start();
                        }
                    });
                }
                else
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        statusText.Text = "Finger detected, running...";

                        assistTimer.Stop();
                        if (timerCheck.IsChecked == false)
                        {
                            timerCheck.IsChecked = true;
                        }
                    });
                }
            }

            if (!status.Contains("HR=0") ||
                !status.Contains("SPO2=0%"))
            {
                this.Dispatcher.Invoke(() =>
                {
                    // Check HR and SPO2
                    spo2 = spo2.Split('%')[0];
                    Check_HRSPO2(hRate, spo2);
                });
            }


            // Display values
            this.Dispatcher.Invoke(() =>
            {
                heartRateVal.Text = hRate + "bpm";
                spo2Val.Text = spo2;
            });

            return flag;
        }

        public void Run()
        {
            button.Content = "Stop";
            statusText.Text = "Running...";

            if (useTimer)
            {
                // start timer
                MainTimerCtl("on");
                statusText.Text = "Running, timer started...";
            }

            OpenPort();
            read = new Thread(() =>
            {
                while (true)
                {
                    if (!serialPort.IsOpen)
                    {
                        return;
                    }
                    RefreshStatus(ReadPort(serialPort));
                    Thread.Sleep(66);
                }
            });
            read.Start();

        }

        public void Stop()
        {
            if (useTimer)
            {
                // stop timer
                MainTimerCtl("off");
                timerCheck.IsChecked = false;
            }

            ClosePort();
            heartRateVal.Text = "0bpm";
            spo2Val.Text = "0%";
            button.Content = "Start";
            statusText.Text = "Ready";
        }

        // what happens after timeout
        private void MainTimer_Tick(object sender, EventArgs e)
        {
            string locker = Environment.GetEnvironmentVariable("windir") + @"\System32\rundll32.exe";
            Process.Start(locker, "user32.dll,LockWorkStation");
        }

        private void AssistTimer_Tick(object sender, EventArgs e)
        {
            if (mainTimer.IsEnabled)
            {
                timerCheck.IsChecked = false;
            }
        }

        // switch on/off the timer
        private bool MainTimerCtl(string switchAction)
        {
            bool flag = false;
            switch (switchAction)
            {
                case "on":
                    mainTimer.Tick += new EventHandler(MainTimer_Tick);
                    mainTimer.Interval = new TimeSpan(0, 0, timeout);
                    mainTimer.Start();

                    statusText.Text = "Timer started";

                    if (serialPort.IsOpen)
                    {
                        statusText.Text = "Running, timer started...";
                    }
                    flag = true;
                    break;
                case "off":
                    mainTimer.Stop();
                    statusText.Text = "Timer canceled";
                    break;
            }

            return flag;
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
            string msg = "Are you sure?";
            MessageBoxResult result =
              MessageBox.Show(
                msg,
                "Quiting",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
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
            MainTimerCtl("off");
        }

        private void TimerCheck_Checked(object sender, RoutedEventArgs e)
        {
            useTimer = true;
            MainTimerCtl("on");
        }

        private void TimeoutVal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!Int32.TryParse(timeoutVal.Text, out timeout) ||
                timeoutVal.Text.Length > 3)
            {
                timeoutVal.Text = "30";
                timeout = 30;
            }

            timerCheck.IsChecked = false;
        }

        private void FingerDection_Checked(object sender, RoutedEventArgs e)
        {
            // nothing to do, since `IsChecked` is enough
        }

        // read config file and decide if need to minimize window on start
        private bool CheckMinimize()
        {
            bool minimize = false;
            if (!File.Exists(@".\demo.conf"))
            {
                return minimize;
            }

            StreamReader file = new StreamReader(@".\demo.conf");
            string line;

            while ((line = file.ReadLine()) != null)
            {
                if (line.Trim() == "minimize")
                {
                    file.Close();
                    minimize = true;
                    break;
                }
            }
            return minimize;
        }

        private void Minimize_Checked(object sender, RoutedEventArgs e)
        {
            StreamWriter fileW = new StreamWriter(@".\demo.conf");
            fileW.WriteLine("minimize");
            fileW.Close();

            string msg = "Going to background...\nYou will have to end this app in task manager yourself";
            MessageBoxResult result =
              MessageBox.Show(
                msg,
                "Proceed?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                minimizeCheck.IsChecked = false;
                return;
            }
            Hide();
        }

        private void Minimize_Unchecked(object sender, RoutedEventArgs e)
        {
            StreamWriter fileW = new StreamWriter(@".\demo.conf");
            fileW.Write("");
            fileW.Close();
        }
    }
}
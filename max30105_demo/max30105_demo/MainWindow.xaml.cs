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
            // 初始化窗口
            InitializeComponent();

            // 如果配置文件写了启动不显示窗口，则勾选后台运行选项，隐藏窗口
            if (CheckMinimize())
            {
                minimizeCheck.IsChecked = true;
            }

            fingerDection.IsChecked = true; // 默认使用手指感应功能
            FindPorts(); // 寻找串口连接的传感器
            Run(); // run方法负责启动程序主要功能
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

        // 用于检测血氧和心率值是否符合报警条件
        private void Check_HRSPO2(string hr, string spo2)
        {
            int hrVal = Int32.Parse(hr);
            int spo2Val = Int32.Parse(spo2);

            // 这里是报警范围
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
            // 有异常数据则记录在status框里
            Outbox.Focus();
            Outbox.CaretIndex = Outbox.Text.Length;
            Outbox.ScrollToEnd();

            // 任意一个值累计达到30个异常之后，弹出报警对话框
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

        // 用来刷新实时的心率血氧值显示
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
                // 如果开了手指感应，知会一下本方法
            });

            // 处理串口数据，使之符合输出格式要求
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

            // 如果选了手指感应，这里探测一波有没有手指
            if (fingerDectionChecked)
            {
                if (status.Contains("HR=0")) // 只要含有0值，那么很显然没有手指
                {
                    //return flag;
                    flag = false;

                    this.Dispatcher.Invoke(() =>
                    {
                        statusText.Text = "No finger";

                        // 这里为了避免手指短暂离开传感器造成不必要的计时终止，设置了延时停止计时的机制
                        if (!assistTimer.IsEnabled)
                        {
                            assistTimer.Interval= new TimeSpan(0, 0, 5);
                            assistTimer.Tick += new EventHandler(AssistTimer_Tick);
                            assistTimer.Start();
                        }
                    });
                }
                else // 不然的话就是有手指了，那就开始运行 (这里说的运行其实就是显示数值加监控加倒计时锁屏)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        statusText.Text = "Finger detected, running...";

                        assistTimer.Stop();
                        if (timerCheck.IsChecked == false)
                        {
                            timerCheck.IsChecked = true; // 这里自动勾选上倒计时锁屏
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
                    // 这里来检查数值是否可以触发报警
                    spo2 = spo2.Split('%')[0];
                    Check_HRSPO2(hRate, spo2);
                });
            }


            // Display values
            // 显示数值
            this.Dispatcher.Invoke(() =>
            {
                heartRateVal.Text = hRate + "bpm";
                spo2Val.Text = spo2;
            });

            return flag;
        }

        // 本方法是程序启动默认运行的流程
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

            OpenPort(); // 打开串口进行读取
            // 这里开了个独立线程去读取并显示数值
            read = new Thread(() =>
            {
                while (true)
                {
                    if (!serialPort.IsOpen) // 串口没开就回去歇着吧
                    {
                        return;
                    }
                    RefreshStatus(ReadPort(serialPort));
                    Thread.Sleep(66); // 每66毫秒刷新一次
                }
            });
            read.Start(); // 启动这个线程

        }

        // 本方法用来停止，对应停止按钮
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
        // 锁屏倒计时
        private void MainTimer_Tick(object sender, EventArgs e)
        {
            string locker = Environment.GetEnvironmentVariable("windir") + @"\System32\rundll32.exe";
            Process.Start(locker, "user32.dll,LockWorkStation");
        }

        // 避免暂时离开手指中断计时的延时计时器
        private void AssistTimer_Tick(object sender, EventArgs e)
        {
            if (mainTimer.IsEnabled)
            {
                timerCheck.IsChecked = false;
            }
        }

        // switch on/off the timer
        // 这个是锁屏计时器的开关方法
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

        // 点了按钮会发生啥
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
        // 读取配置文件，来决定是不是启动时就隐藏窗口
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

        // 如果想取消启动时隐藏窗口的功能，那就清空配置文件
        private void Minimize_Unchecked(object sender, RoutedEventArgs e)
        {
            StreamWriter fileW = new StreamWriter(@".\demo.conf");
            fileW.Write("");
            fileW.Close();
        }
    }
}

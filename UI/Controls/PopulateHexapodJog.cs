using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using liveChartForm;
using OxyPlot;
using Serilog;
using Serilog.Core;
using KoreEngine.Hardware.Hexapod;
namespace KoreEngine.UI.Controls
{
    public class PopulateHexapodJog
    {
        // Event delegate
        public delegate void AnalogDataUpdatedHandler(object sender, AnalogDataUpdatedEventArgs e);

        // Event
        public event AnalogDataUpdatedHandler AnalogDataUpdated;

        private Control parentControl;
        private PIConnection piConnection;
        private Button[] jogButtons = new Button[12];
        private Label[] positionLabels = new Label[6];
        private Label labelCh5, labelCh6, labelPICH5, labelPICH6, labelElapsedTime;
        private string[] axes = { "X", "Y", "Z", "U", "V", "W" };
        private ListBox micronListBox;
        
        private Dictionary<string, double[]> movementMap;
        private double selectedStepSize = 0.0001; // Default to 0.1 micron
        private readonly ILogger _logger;
        //private RealtimeDataClass realtimeData;

        private PictureBox statusPictureBox; // PictureBox to indicate connection status

        private int _nID;

        public string Name;

        public HexapodGCS gcs { get; private set; }

        //public PopulateHexapodJog(Control parent,int nId, PIConnection connection, RealtimeDataClass realtimeData,string name, ILogger logger)
        public PopulateHexapodJog(Control parent, int nId, PIConnection connection,  string name, ILogger logger)

        {
            _logger = logger;
            _logger= _logger.ForContext<PopulateHexapodJog>();

            parentControl = parent;
            piConnection = connection;
            //this.realtimeData = realtimeData;
            _nID = nId;
            Name=name;



            InitializeComponents(_nID); // Call InitializeComponents first
            gcs = new HexapodGCS(name,_logger);
            //gcs.Connect(piConnection.IPAddress, piConnection.Port);
            
            gcs.PrintControllerIdentification();

            gcs.PositionUpdated += OnPositionUpdated;
            gcs.StartRealTimePositionUpdates(1000); // Update every 150ms

            gcs.AnalogInputValuesUpdated += Gcs_AnalogInputValuesUpdated;
            InitializeMovementMap();



        }



        private void Gcs_AnalogInputValuesUpdated(object sender, (double ch5val, double ch6val, TimeSpan elapsed) e)
        {
            // Update UI elements with the received values
            if (parentControl.InvokeRequired)
            {
                parentControl.Invoke((MethodInvoker)delegate
                {
                    UpdateAnalogInputValues(e);
                });
            }
            else
            {
                UpdateAnalogInputValues(e);
            }
        }

        private void UpdateAnalogInputValues((double ch5val, double ch6val, TimeSpan elapsed) e)
        {
            labelCh5.Text = $"CH5: {e.ch5val}";
            labelCh6.Text = $"CH6: {e.ch6val}";
            chartCh5.UpdateChart(e.ch5val);
            chartCh6.UpdateChart(e.ch6val);

            //realtimeData.SetValueByName("PICH5", e.ch5val);
            //realtimeData.SetValueByName("PICH6", e.ch6val);

            labelPICH5.Text = $"PICH5: {e.ch5val:F3}";
            labelPICH6.Text = $"PICH6: {e.ch6val:F3}";

            labelElapsedTime.Text = $"Elapsed Time: {e.elapsed.TotalMilliseconds:F0} ms"; // Update elapsed time

            // Raise the AnalogDataUpdated event
            AnalogDataUpdated?.Invoke(this, new AnalogDataUpdatedEventArgs(e.ch5val, e.ch6val, e.elapsed));

        }

        private void OnPositionUpdated(double[] positions)
        {
            if (parentControl == null || parentControl.IsDisposed)
            {
                // Log that the parent control is null or disposed
                return;
            }

            try
            {
                if (parentControl.InvokeRequired)
                {
                    parentControl.BeginInvoke(new Action(() => SafeUpdatePositionLabels(positions)));
                }
                else
                {
                    SafeUpdatePositionLabels(positions);
                }
            }
            catch (InvalidAsynchronousStateException ex)
            {
                // Log the exception
                // For example: _logger.Error(ex, "InvalidAsynchronousStateException in OnPositionUpdated");
            }
            catch (ObjectDisposedException ex)
            {
                // Log the exception
                // For example: _logger.Error(ex, "ObjectDisposedException in OnPositionUpdated");
            }
            catch (InvalidOperationException ex)
            {
                // Log the exception
                // For example: _logger.Error(ex, "InvalidOperationException in OnPositionUpdated");
            }
        }

        private void SafeUpdatePositionLabels(double[] positions)
        {
            if (parentControl == null || parentControl.IsDisposed)
            {
                return;
            }

            UpdatePositionLabels(positions);
        }

        private liveChart chartCh5 = new liveChart();
        private liveChart chartCh6 = new liveChart();

        public void InitializeLiveChart()
        {
            // Retrieve the screen width and height
            int screenWidth = Screen.PrimaryScreen.Bounds.Width;
            int screenHeight = Screen.PrimaryScreen.Bounds.Height;

            // Calculate the desired width and height for the charts
            int chartWidth = (int)(screenWidth * 0.25);
            int chartHeight = (int)(screenHeight * 0.25);

            // Initialize and configure chartCh5
            chartCh5 = new liveChart();
            chartCh5.SetTitle("PI Analog CH 5");
            chartCh5.Size = new Size(chartWidth, chartHeight);
            chartCh5.StartPosition = FormStartPosition.Manual;
            chartCh5.Location = new Point((int)(screenWidth * 0.75), 0); // Position at x = 75% of screen width, y = 0

            // Initialize and configure chartCh6
            chartCh6 = new liveChart();
            chartCh6.SetTitle("PI Analog CH 6");
            chartCh6.Size = new Size(chartWidth, chartHeight);
            chartCh6.StartPosition = FormStartPosition.Manual;
            chartCh6.Location = new Point((int)(screenWidth * 0.75), chartHeight); // Position below chartCh5

            // Show the charts after setting their properties
            chartCh5.Show();
            chartCh6.Show();
        }



        public void StartContinuousAnalogInputReading()
        {
            if (gcs.ControllerId >= 0)
            {
                gcs.StartContinuousAnalogInputReading();
            }
            
        }

        public void StopAnalogUpdate()
        {
            gcs.StopContinuousAnalogInputReading();
        }

        private void InitializeComponents(int _nID)
        {
            // Label to display the hexapod name and IP address
            Label hexapodNameLabel = new Label
            {
                Name = $"hexapodNameLabel_{_nID}",
                Text = $"Hexapod: {piConnection.IPAddress} (Port: {piConnection.Port})",
                Width = 300,
                Height = 30,
                Location = new System.Drawing.Point(10, 10)
            };
            parentControl.Controls.Add(hexapodNameLabel);

            // Connection status PictureBox
            statusPictureBox = new PictureBox
            {
                Size = new Size(10, 10),
                Location = new System.Drawing.Point(320, 10),
                BackColor = Color.Red // Initial status is disconnected
            };
            parentControl.Controls.Add(statusPictureBox);

            // Create Jog Buttons
            for (int i = 0; i < axes.Length; i++)
            {
                // Plus Button
                jogButtons[i * 2] = new Button
                {
                    Name = $"{axes[i]}PlusButton_{_nID}",
                    Text = $"{axes[i]}+",
                    Width = 75,
                    Height = 30,
                    Location = new System.Drawing.Point(10, 50 + (i * 40))
                };
                jogButtons[i * 2].Click += JogButton_Click;
                parentControl.Controls.Add(jogButtons[i * 2]);

                // Minus Button
                jogButtons[i * 2 + 1] = new Button
                {
                    Name = $"{axes[i]}MinusButton_{_nID}",
                    Text = $"{axes[i]}-",
                    Width = 75,
                    Height = 30,
                    Location = new System.Drawing.Point(90, 50 + (i * 40))
                };
                jogButtons[i * 2 + 1].Click += JogButton_Click;
                parentControl.Controls.Add(jogButtons[i * 2 + 1]);
            }

            // Create Position Labels
            for (int i = 0; i < axes.Length; i++)
            {
                positionLabels[i] = new Label
                {
                    Name = $"{axes[i]}PositionLabel_{_nID}",
                    Text = $"{axes[i]}: 0.00",
                    Width = 100,
                    Height = 30,
                    Location = new System.Drawing.Point(170, 50 + (i * 40))
                };
                parentControl.Controls.Add(positionLabels[i]);
            }

            // Create ListBox for micron values
            micronListBox = new ListBox
            {
                Name = $"micronListBox_{_nID}",
                Width = 150,
                Height = 200,
                Location = new System.Drawing.Point(300, 40)
            };
            parentControl.Controls.Add(micronListBox);

            // Add items to the ListBox
            var micronValues = new (string Text, double Value)[]
            {
        ("0.1 micron", 0.0001),
        ("0.2 micron", 0.0002),
        ("0.5 micron", 0.0005),
        ("1 micron", 0.001),
        ("2 micron", 0.002),
        ("3 micron", 0.003),
        ("4 micron", 0.004),
        ("5 micron", 0.005),
        ("10 micron", 0.01),
        ("20 micron", 0.02),
        ("30 micron", 0.03),
        ("40 micron", 0.04),
        ("50 micron", 0.05),
        ("100 micron", 0.1),
        ("200 micron", 0.2),
        ("300 micron", 0.3)
            };

            foreach (var (text, value) in micronValues)
            {
                micronListBox.Items.Add(new ListBoxItem { Text = text, Value = value });
            }

            micronListBox.SelectedIndexChanged += MicronListBox_SelectedIndexChanged;
            micronListBox.SelectedIndex = 0;

            // Create a GroupBox for CH5, CH6, PICH5, PICH6, and elapsed time labels
            GroupBox groupBox = new GroupBox
            {
                Name = $"groupBox_{_nID}",
                Text = "Analog Input Values",
                Location = new Point(10, 300),
                Width = 250,
                Height = 250
            };

            // Initialize and add labels for CH5, CH6, PICH5, PICH6, and elapsed time
            labelCh5 = new Label { Name = $"labelCh5_{_nID}", Location = new Point(10, 20), Width = 150, Height = 30 };
            labelCh6 = new Label { Name = $"labelCh6_{_nID}", Location = new Point(10, 50), Width = 150, Height = 30 };
            labelPICH5 = new Label { Name = $"labelPICH5_{_nID}", Location = new Point(10, 80), Width = 150, Height = 30 };
            labelPICH6 = new Label { Name = $"labelPICH6_{_nID}", Location = new Point(10, 110), Width = 150, Height = 30 };
            labelElapsedTime = new Label { Name = $"labelElapsedTime_{_nID}", Location = new Point(10, 140), Width = 200, Height = 30 };

            groupBox.Controls.Add(labelCh5);
            groupBox.Controls.Add(labelCh6);
            groupBox.Controls.Add(labelPICH5);
            groupBox.Controls.Add(labelPICH6);
            groupBox.Controls.Add(labelElapsedTime);

            parentControl.Controls.Add(groupBox);
        }



        private void MicronListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (micronListBox.SelectedItem is ListBoxItem selectedItem)
            {
                selectedStepSize = selectedItem.Value;
                UpdateMovementMap();
            }
        }

        public void InitializeMovementMap()
        {
            micronListBox.SelectedIndex = 0;
            // Initialize the movement map with zero arrays for all axes
            movementMap = new Dictionary<string, double[]>
            {
                {"X+", new double[6]},
                {"X-", new double[6]},
                {"Y+", new double[6]},
                {"Y-", new double[6]},
                {"Z+", new double[6]},
                {"Z-", new double[6]},
                {"U+", new double[6]},
                {"U-", new double[6]},
                {"V+", new double[6]},
                {"V-", new double[6]},
                {"W+", new double[6]},
                {"W-", new double[6]}
            };

            UpdateMovementMap();
        }

        private void UpdateMovementMap()
        {
            if (movementMap == null)
            {
                InitializeMovementMap();
                return;
            }
            else
            {
                // Update movements for each button with the new selected step size
                movementMap["X+"][0] = selectedStepSize;
                movementMap["X-"][0] = -selectedStepSize;
                movementMap["Y+"][1] = selectedStepSize;
                movementMap["Y-"][1] = -selectedStepSize;
                movementMap["Z+"][2] = selectedStepSize;
                movementMap["Z-"][2] = -selectedStepSize;
                movementMap["U+"][3] = selectedStepSize;
                movementMap["U-"][3] = -selectedStepSize;
                movementMap["V+"][4] = selectedStepSize;
                movementMap["V-"][4] = -selectedStepSize;
                movementMap["W+"][5] = selectedStepSize;
                movementMap["W-"][5] = -selectedStepSize;
            }
        }

        private async void JogButton_Click(object sender, EventArgs e)
        {
            Button clickedButton = sender as Button;
            if (clickedButton != null && movementMap.TryGetValue(clickedButton.Text, out double[] movement))
            {
                _logger.Information("Jog button clicked: {Button}", clickedButton.Text);
                _logger.Information("Movement command: {Movement}", movement);

                await gcs.MoveToRelativeTarget(movement);

                _logger.Information("Movement completed for button: {Button}", clickedButton.Text);
            }
            else
            {
                _logger.Warning("Jog button clicked but no movement found for button: {Button}", clickedButton?.Text);
            }
        }

        public class ListBoxItem
        {
            public string Text { get; set; }
            public double Value { get; set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private void UpdatePositionLabels(double[] positions)
        {
            for (int i = 0; i < axes.Length; i++)
            {
                positionLabels[i].Text = $"{axes[i]}: {positions[i]:F4}";
                //SetLabelStyle(positionLabels[i], Color.Red, FontStyle.Bold, 200);
            }
        }

        public async Task ConnectAsync()
        {
            _logger.Information("Attempting to connect to hexapod at {IPAddress}:{Port}", piConnection.IPAddress, piConnection.Port);

            int result = await Task.Run(() => gcs.Connect(piConnection.IPAddress, piConnection.Port));
            bool connected = result != -1;

            if (connected)
            {
                _logger.Information("Successfully connected to hexapod at {IPAddress}:{Port}", piConnection.IPAddress, piConnection.Port);
            }
            else
            {
                _logger.Error("Failed to connect to hexapod at {IPAddress}:{Port}. Error code: {ErrorCode}", piConnection.IPAddress, piConnection.Port, result);
            }

            UpdateStatus(connected);
        }

        public async Task DisconnectAsync()
        {
            _logger.Information("Attempting to disconnect from hexapod at {IPAddress}:{Port}", piConnection.IPAddress, piConnection.Port);

            await Task.Run(() => gcs.Disconnect());

            _logger.Information("Successfully disconnected from hexapod at {IPAddress}:{Port}", piConnection.IPAddress, piConnection.Port);

            UpdateStatus(false);
        }


        private void UpdateStatus(bool isConnected)
        {
            if (isConnected)
            {
                statusPictureBox.BackColor = Color.Green;
            }
            else
            {
                statusPictureBox.BackColor = Color.Red;
            }
        }
        private async void SetLabelStyle(Label label, Color color, FontStyle fontStyle, int duration)
        {
            var originalColor = label.ForeColor;
            var originalFont = label.Font;

            label.ForeColor = color;
            label.Font = new Font(label.Font, fontStyle);

            await Task.Delay(duration);

            label.ForeColor = originalColor;
            label.Font = originalFont;
        }

        // Define the AnalogDataUpdatedEventArgs class
        public class AnalogDataUpdatedEventArgs : EventArgs
        {
            public double Ch5Value { get; }
            public double Ch6Value { get; }
            public TimeSpan Elapsed { get; }

            public AnalogDataUpdatedEventArgs(double ch5Value, double ch6Value, TimeSpan elapsed)
            {
                Ch5Value = ch5Value;
                Ch6Value = ch6Value;
                Elapsed = elapsed;
            }
        }

        // Public property to access the controller instance
        public HexapodGCS gcsController
        {
            get { return gcs; }
        }
    }
}

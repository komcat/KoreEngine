using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Windows.Forms;
using Serilog.Core;
using Serilog;

namespace KoreEngine.Data
{
    public class SensorChannel
    {
        public int Id { get; set; }
        public double Value { get; set; }
        public double Target { get; set; }
        public string Unit { get; set; }
        public Queue<double> RecentValues { get; set; } = new Queue<double>();
        public double Average { get; set; }

        public void AddValue(double newValue, int maxValues = 3)
        {
            RecentValues.Enqueue(newValue);
            if (RecentValues.Count > maxValues)
            {
                RecentValues.Dequeue();
            }
            Average = RecentValues.Average();
            Value = newValue; // Update the Value property with the latest value
        }
    }

    public class RealtimeDataClass
    {
        private Dictionary<string, SensorChannel> realTimeData;
        private string selectedDataName;
        private TextBox _textbox;
        private Label _lbl;
        private ILogger logger;
        public IReadOnlyDictionary<string, SensorChannel> RealTimeData => realTimeData;

        public RealtimeDataClass(ILogger logger)
        {
            realTimeData = new Dictionary<string, SensorChannel>();
            LoadDataFromJson();
            this.logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="textBox">Textbox for update data</param>
        public void SelectDataName(string name, TextBox textBox, Label lbl)
        {
            selectedDataName = name;
            _textbox = textBox;
            _lbl = lbl;
        }

        public void WireNametoControl(Control control)
        {
            if (control is ComboBox comboBox)
            {
                comboBox.Items.Clear();
                foreach (var key in realTimeData.Keys)
                {
                    comboBox.Items.Add(key);
                }
            }
            else if (control is ListBox listBox)
            {
                listBox.Items.Clear();
                foreach (var key in realTimeData.Keys)
                {
                    listBox.Items.Add(key);
                }
            }
            else
            {
                throw new ArgumentException("Control must be a ComboBox or ListBox.");
            }
        }

        public void SetValueByName(string inputName, double assignValue)
        {
            if (realTimeData.ContainsKey(inputName))
            {
                realTimeData[inputName].AddValue(assignValue);
            }
            else
            {
                throw new ArgumentException($"Key '{inputName}' not found in real-time data.");
            }
        }

        public double GetCurrentLastValue()
        {
            return GetValueByName(this.selectedDataName);
        }

        public double GetValueByName(string inputName)
        {
            if (realTimeData.ContainsKey(inputName))
            {
                return realTimeData[inputName].Value;
            }
            else
            {
                logger.Error("Key '{InputName}' not found in real-time data.", inputName);
                return double.NaN; // or any other default value you deem appropriate
            }
        }

        public double GetTargetByName(string inputName)
        {
            if (realTimeData.ContainsKey(inputName))
            {
                return realTimeData[inputName].Target;
            }
            else
            {
                logger.Error("Key '{InputName}' not found in real-time data.", inputName);
                return double.NaN; // or any other default value you deem appropriate
            }
        }

        public void PopulateControls(Control parent)
        {
            int yPos = 10; // Starting y position for the first control
            int xOffsetLabel = 10; // x position for labels
            int xOffsetTextbox = 150; // x position for textboxes
            int controlHeight = 25; // Height of each control

            foreach (var key in realTimeData.Keys)
            {
                // Create and configure the label
                Label label = new Label
                {
                    Text = key,
                    Location = new System.Drawing.Point(xOffsetLabel, yPos),
                    Width = 130
                };
                parent.Controls.Add(label);

                // Create and configure the textbox
                TextBox textBox = new TextBox
                {
                    Name = "textBox_" + key,
                    Location = new System.Drawing.Point(xOffsetTextbox, yPos),
                    Width = 100,
                    Text = realTimeData[key].Value.ToString("F3")
                };
                parent.Controls.Add(textBox);

                yPos += controlHeight + 5; // Move the position for the next set of controls
            }
        }

        public void UpdateTextboxByName(Control parent, string dataName)
        {
            string textBoxName = "textBox_" + dataName;
            TextBox textBoxToUpdate = parent.Controls.Find(textBoxName, true).FirstOrDefault() as TextBox;

            if (textBoxToUpdate != null)
            {
                if (realTimeData.ContainsKey(dataName))
                {
                    textBoxToUpdate.Text = realTimeData[dataName].Average.ToString("F3");
                }
                else
                {
                    //throw new ArgumentException($"Key '{dataName}' not found in real-time data.");
                }
            }
            else
            {
                //throw new ArgumentException($"Textbox for '{dataName}' not found.");
            }

            if (_textbox != null && !String.IsNullOrEmpty(selectedDataName))
            {
                var selectedChannel = realTimeData[selectedDataName];
                _textbox.Text = FormatValueWithUnit(selectedChannel.Average, selectedChannel.Unit);
                UpdatePassFail(_lbl);
            }
        }

        private string FormatValueWithUnit(double value, string unit)
        {
            string formattedValue = value.ToString("F3");
            string newUnit = unit;

            if (Math.Abs(value) < 1e-9)
            {
                formattedValue = (value * 1e12).ToString("F3");
                newUnit = "p" + unit;
            }
            else if (Math.Abs(value) < 1e-6)
            {
                formattedValue = (value * 1e9).ToString("F3");
                newUnit = "n" + unit;
            }
            else if (Math.Abs(value) < 1e-3)
            {
                formattedValue = (value * 1e6).ToString("F3");
                newUnit = "u" + unit;
            }
            else if (Math.Abs(value) < 1)
            {
                formattedValue = (value * 1e3).ToString("F3");
                newUnit = "m" + unit;
            }

            return $"{formattedValue} {newUnit}";
        }

        private void UpdatePassFail(Label label)
        {
            double percent = 0;
            if (!string.IsNullOrEmpty(selectedDataName) && realTimeData.ContainsKey(selectedDataName))
            {
                var selectedChannel = realTimeData[selectedDataName];
                if (selectedChannel != null && selectedChannel.Target != 0)
                {
                    percent = (selectedChannel.Average / selectedChannel.Target) * 100;
                }

                if (percent < 90)
                {
                    label.Text = $"n/a ({percent:F1}%)";
                    label.BackColor = System.Drawing.Color.Orange;
                    label.ForeColor = System.Drawing.Color.Black; // Set a contrasting text color
                }
                else if (percent >= 90 && percent < 100)
                {
                    label.Text = $"Need Work ({percent:F1}%)";
                    label.BackColor = System.Drawing.Color.Yellow;
                    label.ForeColor = System.Drawing.Color.Black; // Set a contrasting text color
                }
                else if (percent >= 100)
                {
                    label.Text = $"PASS ({percent:F1}%)";
                    label.BackColor = System.Drawing.Color.Green;
                    label.ForeColor = System.Drawing.Color.White; // Set a bright, contrasting text color
                }
            }
            else
            {
                label.Text = "n/a";
                label.BackColor = System.Drawing.Color.Transparent;
                label.ForeColor = System.Drawing.Color.Black; // Reset to default color
            }
        }

        private void LoadDataFromJson()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"config\realtimedataname.json");
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                realTimeData = JsonConvert.DeserializeObject<Dictionary<string, SensorChannel>>(json);
            }
            else
            {
                throw new FileNotFoundException($"JSON file not found at path: {filePath}");
            }
        }
    }
}

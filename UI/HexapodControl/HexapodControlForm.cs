using KoreEngine.Config;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using KoreEngine.Hardware.Hexapod;
using PIConnection = KoreEngine.Config.PIConnection;
using System.Threading;
namespace KoreEngine.UI.HexapodControl
{
    public partial class HexapodControlForm : Form
    {
        private Dictionary<string, HexapodGCS> activeHexapods; // Track all active hexapod connections

        private HexapodGCS hexapod;
        private readonly ILogger logger;
        private System.Windows.Forms.Timer updateTimer;
        private const int UPDATE_INTERVAL = 100; // 100ms refresh rate
        private double jogStepSize = 0.1; // Default step size in mm
        private HexapodConfig config;
        private ComboBox cboHexapodSelect;
        private Label lblStatus;
        private Label lblMovementStatus; // New label for movement status
        private PIConnection currentConnection; // Track current connection
        private ListView lstConnections; // List view for showing all connections
        private CancellationTokenSource cancellationTokenSource;
        private readonly SemaphoreSlim updateLock = new SemaphoreSlim(1, 1);
        private bool isUpdating = false;
        // Constructor
        public HexapodControlForm()
        {
            activeHexapods = new Dictionary<string, HexapodGCS>();
            InitializeFormComponent();
            logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            LoadHexapodConfiguration();
            InitializeUpdateTimer();
            SetupJogStepSizeComboBox();
            cancellationTokenSource = new CancellationTokenSource();
        }
        private HexapodGCS currentHexapod
        {
            get
            {
                string name = GetSelectedHexapodName();
                HexapodGCS hexapod;
                activeHexapods.TryGetValue(name, out hexapod);
                return hexapod;
            }
        }

        private void LoadHexapodConfiguration()
        {
            try
            {
                config = HexapodConfigUtility.LoadConfiguration();
                UpdateHexapodSelectionDropdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading hexapod configuration: {ex.Message}");
                logger.Error(ex, "Failed to load hexapod configuration");
            }
        }
        private void InitializeUpdateTimer()
        {
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = UPDATE_INTERVAL;
            updateTimer.Tick += UpdateTimer_Tick;
        }

        private void SetupJogStepSizeComboBox()
        {
            ComboBox cboStepSize = (ComboBox)Controls.Find("cboStepSize", true)[0];
            double[] stepSizes = { 0.0002, 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02,0.05, 0.1,0.2,0.5, 1.0};
            foreach (double size in stepSizes)
            {
                cboStepSize.Items.Add(size.ToString("F3"));
            }
            cboStepSize.SelectedIndex = 6; // Default to 0.1mm
            cboStepSize.SelectedIndexChanged += CboStepSize_SelectedIndexChanged;
        }
        private void UpdateHexapodSelectionDropdown()
        {
            if (cboHexapodSelect != null && config != null)
            {
                cboHexapodSelect.Items.Clear();
                foreach (var connection in config.Connections)
                {
                    cboHexapodSelect.Items.Add(new HexapodListItem(connection));
                }

                if (cboHexapodSelect.Items.Count > 0)
                {
                    cboHexapodSelect.SelectedIndex = 0;
                }
            }
        }

        private void InitializeFormComponent()
        {
            this.Text = "Hexapod Control Panel";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Connection Group
            GroupBox connectionGroup = new GroupBox()
            {
                Text = "Connection",
                Location = new Point(10, 10),
                Size = new Size(300, 250)
            };

            lstConnections = new ListView()
            {
                Location = new Point(20, 140),
                Size = new Size(260, 100),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };

            lstConnections.Columns.Add("Name", 80);
            lstConnections.Columns.Add("IP", 100);
            lstConnections.Columns.Add("Status", 70);

            connectionGroup.Controls.Add(lstConnections);

            Label lblHexapod = new Label()
            {
                Text = "Select Hexapod:",
                Location = new Point(20, 25),
                Size = new Size(100, 20)
            };

            cboHexapodSelect = new ComboBox()
            {
                Location = new Point(20, 45),
                Size = new Size(260, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboHexapodSelect.SelectedIndexChanged += CboHexapodSelect_SelectedIndexChanged;

            lblStatus = new Label()
            {
                Location = new Point(20, 75),
                Size = new Size(260, 20),
                Text = "Status: Not Connected"
            };

            // Add new movement status label
            lblMovementStatus = new Label()
            {
                Location = new Point(20, 95),
                Size = new Size(260, 20),
                Text = "Movement: ---",
                ForeColor = Color.Blue // Make it stand out
            };

            Button btnConnect = new Button()
            {
                Text = "Connect",
                Location = new Point(20, 115),
                Size = new Size(100, 30)
            };
            btnConnect.Click += BtnConnect_Click;

            Button btnDisconnect = new Button()
            {
                Text = "Disconnect",
                Location = new Point(130, 115),
                Size = new Size(100, 30)
            };
            btnDisconnect.Click += BtnDisconnect_Click;

            // Position Display Group
            GroupBox positionGroup = new GroupBox()
            {
                Text = "Position",
                Location = new Point(10, 270),
                Size = new Size(300, 200)
            };

            string[] axes = { "X", "Y", "Z", "U", "V", "W" };
            for (int i = 0; i < axes.Length; i++)
            {
                Label axisLabel = new Label()
                {
                    Text = $"{axes[i]}:",
                    Location = new Point(20, 30 + i * 25),
                    Size = new Size(30, 20)
                };

                Label positionLabel = new Label()
                {
                    Name = $"lbl{axes[i]}Pos",
                    Text = "0.000",
                    Location = new Point(60, 30 + i * 25),
                    Size = new Size(100, 20)
                };

                positionGroup.Controls.Add(axisLabel);
                positionGroup.Controls.Add(positionLabel);
            }

            // Jog Control Group
            GroupBox jogGroup = new GroupBox()
            {
                Text = "Jog Control",
                Location = new Point(320, 10),
                Size = new Size(450, 360)
            };

            Label lblStepSize = new Label()
            {
                Text = "Step Size (mm):",
                Location = new Point(20, 30),
                Size = new Size(100, 20)
            };

            ComboBox cboStepSize = new ComboBox()
            {
                Name = "cboStepSize",
                Location = new Point(120, 27),
                Size = new Size(100, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            for (int i = 0; i < axes.Length; i++)
            {
                Label axisLabel = new Label()
                {
                    Text = axes[i],
                    Location = new Point(20, 70 + i * 35),
                    Size = new Size(30, 25)
                };

                Button btnMinus = new Button()
                {
                    Text = "-",
                    Name = $"btnMinus{axes[i]}",
                    Location = new Point(60, 65 + i * 35),
                    Size = new Size(40, 25)
                };
                string axis = axes[i];
                btnMinus.Click += (s, e) => JogAxis(axis, -1);

                Button btnPlus = new Button()
                {
                    Text = "+",
                    Name = $"btnPlus{axes[i]}",
                    Location = new Point(110, 65 + i * 35),
                    Size = new Size(40, 25)
                };
                btnPlus.Click += (s, e) => JogAxis(axis, 1);

                jogGroup.Controls.Add(axisLabel);
                jogGroup.Controls.Add(btnMinus);
                jogGroup.Controls.Add(btnPlus);
            }

            jogGroup.Controls.Add(lblStepSize);
            jogGroup.Controls.Add(cboStepSize);

            connectionGroup.Controls.AddRange(new Control[] {
                lblHexapod,
                cboHexapodSelect,
                lblStatus,
                lblMovementStatus,
                btnConnect,
                btnDisconnect
            });

            this.Controls.AddRange(new Control[] {
                connectionGroup,
                positionGroup,
                jogGroup
            });
        }
        private async void UpdateMovementStatus()
        {
            var hexapod = currentHexapod;
            if (hexapod != null && hexapod.IsConnected())
            {
                try
                {
                    int[] isMoving = new int[6];
                    if (GCS2.IsMoving(hexapod.ControllerId, "X Y Z U V W", isMoving) > 0)
                    {
                        bool moving = isMoving.Any(x => x != 0);
                        lblMovementStatus.Text = moving ? "Movement: Moving" : "Movement: Idle";
                        lblMovementStatus.ForeColor = moving ? Color.Red : Color.Green;
                    }
                }
                catch (Exception ex)
                {
                    lblMovementStatus.Text = "Movement: Error";
                    lblMovementStatus.ForeColor = Color.Red;
                    logger.Error(ex, "Error checking movement status");
                }
            }
            else
            {
                lblMovementStatus.Text = "Movement: ---";
                lblMovementStatus.ForeColor = Color.Blue;
            }
        }
        private string GetSelectedHexapodName()
        {
            var selected = cboHexapodSelect.SelectedItem as HexapodListItem;
            return selected?.Connection.Name;
        }

        private void EnableJogControls(bool enable)
        {
            if (!IsDisposed && Created)
            {
                Invoke((MethodInvoker)delegate
                {
                    string[] axes = { "X", "Y", "Z", "U", "V", "W" };
                    foreach (string axis in axes)
                    {
                        var btnMinus = (Button)Controls.Find($"btnMinus{axis}", true)[0];
                        var btnPlus = (Button)Controls.Find($"btnPlus{axis}", true)[0];
                        btnMinus.Enabled = enable;
                        btnPlus.Enabled = enable;
                    }
                });
            }
        }

        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            var selectedItem = cboHexapodSelect.SelectedItem as HexapodListItem;
            if (selectedItem == null)
            {
                MessageBox.Show("Please select a hexapod to connect.");
                return;
            }

            try
            {
                if (activeHexapods.TryGetValue(selectedItem.Connection.Name, out var existingHexapod))
                {
                    if (existingHexapod.IsConnected())
                    {
                        MessageBox.Show("Already connected to this hexapod.");
                        return;
                    }
                    else
                    {
                        existingHexapod.Dispose();
                        activeHexapods.Remove(selectedItem.Connection.Name);
                    }
                }

                var newHexapod = new HexapodGCS(selectedItem.Connection.Name, logger);

                // Connect in background thread
                int result = await Task.Run(() =>
                    newHexapod.Connect(selectedItem.Connection.IPAddress, selectedItem.Connection.Port)
                );

                if (result >= 0)
                {
                    activeHexapods[selectedItem.Connection.Name] = newHexapod;
                    MessageBox.Show($"Connected successfully to {selectedItem}!");

                    if (!updateTimer.Enabled)
                        updateTimer.Start();
                }
                else
                {
                    MessageBox.Show($"Failed to connect to {selectedItem}.");
                    newHexapod.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error connecting: {ex.Message}");
                logger.Error(ex, "Connection error");
            }
            finally
            {
                UpdateConnectionsList();
                UpdateStatusLabel();
            }
        }
        private async void BtnDisconnect_Click(object sender, EventArgs e)
        {
            var selectedItem = cboHexapodSelect.SelectedItem as HexapodListItem;
            if (selectedItem == null) return;

            if (activeHexapods.TryGetValue(selectedItem.Connection.Name, out var hexapod))
            {
                try
                {
                    await Task.Run(() =>
                    {
                        hexapod.Disconnect();
                        hexapod.Dispose();
                    });

                    activeHexapods.Remove(selectedItem.Connection.Name);
                    MessageBox.Show($"Disconnected from {selectedItem.Connection.Name}");

                    if (activeHexapods.Count == 0)
                        updateTimer.Stop();
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error during disconnection");
                    MessageBox.Show($"Error disconnecting: {ex.Message}");
                }
                finally
                {
                    UpdateConnectionsList();
                    UpdateStatusLabel();
                }
            }
        }
        private void DisconnectHexapod()
        {
            try
            {
                updateTimer.Stop();
                if (hexapod != null)
                {
                    hexapod.Disconnect();
                    hexapod.Dispose();
                    hexapod = null;
                }
                currentConnection = null; // Clear current connection
                UpdateStatusLabel();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during disconnection");
            }
        }
        private void CboHexapodSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateStatusLabel();
            UpdatePositionDisplay(); // Update position display for newly selected hexapod
        }
        private void UpdateConnectionsList()
        {
            lstConnections.Items.Clear();
            foreach (var connection in config.Connections)
            {
                var item = new ListViewItem(connection.Name);
                item.SubItems.Add(connection.IPAddress);

                if (activeHexapods.TryGetValue(connection.Name, out var hexapod))
                {
                    item.SubItems.Add(hexapod.IsConnected() ? "Connected" : "Error");
                    if (hexapod.IsConnected())
                        item.BackColor = Color.LightGreen;
                }
                else
                {
                    item.SubItems.Add("Not Connected");
                }

                lstConnections.Items.Add(item);
            }
        }
        private void UpdateStatusLabel()
        {
            var selectedItem = cboHexapodSelect.SelectedItem as HexapodListItem;
            if (selectedItem != null)
            {
                bool isConnected = activeHexapods.TryGetValue(selectedItem.Connection.Name, out var hexapod)
                    && hexapod.IsConnected();

                lblStatus.Text = $"Selected: {selectedItem.Connection.Name} - {(isConnected ? "Connected" : "Not Connected")}";
            }
            else
            {
                lblStatus.Text = "Status: No hexapod selected";
            }
        }

        private async Task UpdatePositionDisplayAsync()
        {
            var hexapod = currentHexapod;
            if (hexapod != null && hexapod.IsConnected())
            {
                try
                {
                    double[] positions = await Task.Run(() => hexapod.GetPosition());

                    if (!IsDisposed && Created)
                    {
                        Invoke((MethodInvoker)delegate
                        {
                            string[] axes = { "X", "Y", "Z", "U", "V", "W" };
                            for (int i = 0; i < axes.Length; i++)
                            {
                                Label lbl = (Label)Controls.Find($"lbl{axes[i]}Pos", true)[0];
                                lbl.Text = positions[i].ToString("F3");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error updating position display");
                }
            }
            else
            {
                if (!IsDisposed && Created)
                {
                    Invoke((MethodInvoker)delegate
                    {
                        string[] axes = { "X", "Y", "Z", "U", "V", "W" };
                        foreach (string axis in axes)
                        {
                            Label lbl = (Label)Controls.Find($"lbl{axis}Pos", true)[0];
                            lbl.Text = "-.---";
                        }
                    });
                }
            }
        }
        private async Task UpdateMovementStatusAsync()
        {
            if (isUpdating) return;

            try
            {
                await updateLock.WaitAsync();
                isUpdating = true;

                var hexapod = currentHexapod;
                if (hexapod != null && hexapod.IsConnected())
                {
                    try
                    {
                        int[] isMoving = new int[6];
                        await Task.Run(() =>
                        {
                            return GCS2.IsMoving(hexapod.ControllerId, "X Y Z U V W", isMoving);
                        });

                        if (!IsDisposed && Created)
                        {
                            Invoke((MethodInvoker)delegate
                            {
                                bool moving = isMoving.Any(x => x != 0);
                                lblMovementStatus.Text = moving ? "Movement: Moving" : "Movement: Idle";
                                lblMovementStatus.ForeColor = moving ? Color.Red : Color.Green;
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!IsDisposed && Created)
                        {
                            Invoke((MethodInvoker)delegate
                            {
                                lblMovementStatus.Text = "Movement: Error";
                                lblMovementStatus.ForeColor = Color.Red;
                            });
                        }
                        logger.Error(ex, "Error checking movement status");
                    }
                }
                else
                {
                    if (!IsDisposed && Created)
                    {
                        Invoke((MethodInvoker)delegate
                        {
                            lblMovementStatus.Text = "Movement: ---";
                            lblMovementStatus.ForeColor = Color.Blue;
                        });
                    }
                }
            }
            finally
            {
                isUpdating = false;
                updateLock.Release();
            }
        }
        private async void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (cancellationTokenSource.Token.IsCancellationRequested)
                return;

            try
            {
                await Task.WhenAll(
                    UpdatePositionDisplayAsync(),
                    UpdateMovementStatusAsync()
                );

                UpdateConnectionsList(); // This is UI-only operation, no need for async
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in timer update");
            }
        }
        private void UpdatePositionDisplay()
        {
            var hexapod = currentHexapod;
            if (hexapod != null && hexapod.IsConnected())
            {
                try
                {
                    double[] positions = hexapod.GetPosition();
                    string[] axes = { "X", "Y", "Z", "U", "V", "W" };

                    for (int i = 0; i < axes.Length; i++)
                    {
                        Label lbl = (Label)Controls.Find($"lbl{axes[i]}Pos", true)[0];
                        lbl.Text = positions[i].ToString("F3");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error updating position display");
                }
            }
            else
            {
                // Clear position display when no active hexapod is selected
                string[] axes = { "X", "Y", "Z", "U", "V", "W" };
                foreach (string axis in axes)
                {
                    Label lbl = (Label)Controls.Find($"lbl{axis}Pos", true)[0];
                    lbl.Text = "-.---";
                }
            }
        }
        private async void JogAxis(string axis, int direction)
        {
            var hexapod = currentHexapod;
            if (hexapod != null && hexapod.IsConnected())
            {
                try
                {
                    // Disable jog buttons during movement
                    EnableJogControls(false);

                    double[] movement = new double[6] { 0, 0, 0, 0, 0, 0 };
                    int axisIndex = "XYZUVW".IndexOf(axis);
                    movement[axisIndex] = jogStepSize * direction;

                    await Task.Run(async () =>
                    {
                        await hexapod.MoveToRelativeTarget(movement);
                    });
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error jogging {axis} axis");
                    if (!IsDisposed && Created)
                    {
                        Invoke((MethodInvoker)delegate
                        {
                            MessageBox.Show($"Error jogging {axis} axis: {ex.Message}");
                        });
                    }
                }
                finally
                {
                    // Re-enable jog buttons after movement
                    EnableJogControls(true);
                }
            }
        }
        private void CboStepSize_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboBox cbo = (ComboBox)sender;
            if (double.TryParse(cbo.SelectedItem.ToString(), out double newStepSize))
            {
                jogStepSize = newStepSize;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cancellationTokenSource.Cancel();

            // Stop the update timer
            updateTimer.Stop();
            updateTimer.Dispose();

            // Cleanup all active connections
            foreach (var hexapod in activeHexapods.Values)
            {
                try
                {
                    Task.Run(() =>
                    {
                        hexapod.Disconnect();
                        hexapod.Dispose();
                    }).Wait(1000); // Give each disconnection up to 1 second
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error disconnecting hexapod during shutdown");
                }
            }
            activeHexapods.Clear();

            updateLock.Dispose();
            cancellationTokenSource.Dispose();

            base.OnFormClosing(e);
        }
    }

    public class HexapodListItem
    {
        public PIConnection Connection { get; }

        public HexapodListItem(PIConnection connection)
        {
            Connection = connection;
        }

        public override string ToString()
        {
            return $"{Connection.Name} ({Connection.IPAddress}:{Connection.Port})";
        }
    }
}

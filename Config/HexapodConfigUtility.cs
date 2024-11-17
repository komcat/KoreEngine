// KoreEngine/Config/HexapodConfigUtility.cs
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace KoreEngine.Config
{
    public class HexapodConfig
    {
        public List<PIConnection> Connections { get; set; } = new List<PIConnection>();

        public static string ConfigDirectory => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "KoreEngine",
            "Config"
        );
        public static string ConfigPath => Path.Combine(ConfigDirectory, "hexapod_config.json");
    }

    public class PIConnection
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("ipAddress")]
        public string IPAddress { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }
    }

    public static class HexapodConfigUtility
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include,
            Error = (sender, args) =>
            {
                Console.WriteLine($"JSON Error: {args.ErrorContext.Error.Message}");
                args.ErrorContext.Handled = true;
            }
        };

        public static HexapodConfig LoadConfiguration()
        {
            try
            {
                if (!Directory.Exists(HexapodConfig.ConfigDirectory))
                {
                    Directory.CreateDirectory(HexapodConfig.ConfigDirectory);
                }

                if (!File.Exists(HexapodConfig.ConfigPath))
                {
                    var defaultConfig = CreateDefaultConfiguration();
                    SaveConfiguration(defaultConfig);
                    return defaultConfig;
                }

                string jsonString = File.ReadAllText(HexapodConfig.ConfigPath);
                var config = JsonConvert.DeserializeObject<HexapodConfig>(jsonString, JsonSettings);

                // Validate loaded configuration
                if (config?.Connections == null || !config.Connections.Any())
                {
                    Console.WriteLine("Invalid or empty configuration loaded, creating default");
                    return CreateDefaultConfiguration();
                }

                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading hexapod configuration: {ex.Message}");
                return CreateDefaultConfiguration();
            }
        }

        public static void SaveConfiguration(HexapodConfig config)
        {
            try
            {
                string jsonString = JsonConvert.SerializeObject(config, JsonSettings);
                File.WriteAllText(HexapodConfig.ConfigPath, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving hexapod configuration: {ex.Message}");
            }
        }

        private static HexapodConfig CreateDefaultConfiguration()
        {
            var settings = Properties.Settings.Default;
            var properties = settings.GetType().GetProperties();

            var connections = new List<PIConnection>();

            var ipProperties = properties
                .Where(p => p.Name.ToLower().Contains("hexapodipaddress"))
                .ToList();

            foreach (var ipProperty in ipProperties)
            {
                // Extract the identifier part after "hexapodIpaddress"
                string identifier = ipProperty.Name;
                if (identifier.StartsWith("hexapodIpaddress", StringComparison.OrdinalIgnoreCase))
                {
                    identifier = identifier.Substring("hexapodIpaddress".Length);
                }

                // Find corresponding port property
                var portProperty = properties.FirstOrDefault(p =>
                    p.Name.Equals($"hexapodIpport{identifier}", StringComparison.OrdinalIgnoreCase));

                if (portProperty != null)
                {
                    var ipValue = ipProperty.GetValue(settings)?.ToString();
                    var portValue = portProperty.GetValue(settings);

                    if (!string.IsNullOrEmpty(ipValue) && portValue != null)
                    {
                        connections.Add(new PIConnection
                        {
                            Name = identifier,
                            IPAddress = ipValue,
                            Port = Convert.ToInt32(portValue)
                        });
                    }
                }
            }

            return new HexapodConfig { Connections = connections };
        }
        public static bool ValidateConfiguration(HexapodConfig config)
        {
            if (config?.Connections == null || !config.Connections.Any())
                return false;

            foreach (var connection in config.Connections)
            {
                if (string.IsNullOrWhiteSpace(connection.Name) ||
                    string.IsNullOrWhiteSpace(connection.IPAddress) ||
                    connection.Port <= 0 || connection.Port > 65535)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static class PIConnectionArrayExtensions
    {
        public static PIConnection[] ToConnectionArray(this List<PIConnection> connections)
        {
            return connections?.ToArray() ?? Array.Empty<PIConnection>();
        }

        public static PIConnection GetConnectionByName(this PIConnection[] connections, string name)
        {
            return connections?.FirstOrDefault(c =>
                c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
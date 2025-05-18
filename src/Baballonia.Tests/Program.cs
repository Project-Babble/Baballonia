using System.IO.Ports;
using System.Text;
using System.Text.Json;

namespace Baballonia.Tests;

class Program
{
    // Payload structure classes
    public class WifiData
    {
        public string ssid { get; set; }
        public string password { get; set; }
    }

    public class MdnsData
    {
        public string hostname { get; set; }
    }

    public class Command
    {
        public string command { get; set; }
        public object data { get; set; }
    }

    public class Payload
    {
        public Command[] commands { get; set; }
    }

    static void Main(string[] args)
    {
        Console.WriteLine("ESP32-S3 Wi-Fi Setup Tool");
        Console.WriteLine("-------------------------");

        // Get serial port information
        string portName = GetSerialPort();
        const int baudRate = 921600;

        // Get Wi-Fi credentials
        Console.Write("Enter Wi-Fi SSID: ");
        string ssid = Console.ReadLine();

        Console.Write("Enter Wi-Fi Password: ");
        string password = Console.ReadLine();

        // Get mDNS hostname (optional)
        Console.Write("Enter mDNS Hostname (press Enter to skip): ");
        string hostname = Console.ReadLine();

        // Create payload
        Payload payload = new Payload
        {
            commands =
            [
                new Command
                {
                    command = "set_wifi",
                    data = new WifiData { ssid = ssid, password = password }
                }
            ]
        };

        // Add mDNS command if hostname is provided
        if (!string.IsNullOrWhiteSpace(hostname))
        {
            payload.commands = payload.commands.Append(new Command
            {
                command = "set_mdns",
                data = new MdnsData { hostname = hostname }
            }).ToArray();
        }

        // Serialize payload to JSON
        string jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        Console.WriteLine("\nJSON Payload:");
        Console.WriteLine(jsonPayload);

        // Confirm before sending
        Console.Write("\nSend payload to ESP32-S3? (y/n): ");
        if (Console.ReadLine().ToLower() != "y")
        {
            Console.WriteLine("Operation canceled.");
            return;
        }

        try
        {
            // Send the payload over serial
            SendSerialPayload(portName, baudRate, jsonPayload);
            Console.WriteLine("Payload sent successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending payload: {ex.Message}");
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    static string GetSerialPort()
    {
        string[] availablePorts = SerialPort.GetPortNames();

        if (availablePorts.Length == 0)
        {
            Console.WriteLine("No serial ports found. Please connect your ESP32-S3.");
            Environment.Exit(1);
        }

        Console.WriteLine("Available serial ports:");
        for (int i = 0; i < availablePorts.Length; i++)
        {
            Console.WriteLine($"{i + 1}. {availablePorts[i]}");
        }

        int selection;
        do
        {
            Console.Write("Select a port (enter number): ");
        } while (!int.TryParse(Console.ReadLine(), out selection) ||
                 selection < 1 ||
                 selection > availablePorts.Length);

        return availablePorts[selection - 1];
    }

    static void SendSerialPayload(string portName, int baudRate, string payload)
    {
        // Create a new SerialPort object with the specified port name and baud rate
        using SerialPort serialPort = new SerialPort(portName, baudRate);

        // Set serial port parameters
        serialPort.DataBits = 8;
        serialPort.StopBits = StopBits.One;
        serialPort.Parity = Parity.None;
        serialPort.Handshake = Handshake.None;

        // Set read/write timeouts
        serialPort.ReadTimeout = 2000;
        serialPort.WriteTimeout = 2000;

        try
        {
            // Open the port
            serialPort.Open();
            Console.WriteLine($"Connected to {portName} at {baudRate} baud rate");

            // Convert the payload to bytes
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

            // Write the payload to the serial port
            serialPort.Write(payloadBytes, 0, payloadBytes.Length);

            // Add a newline to indicate end of message
            serialPort.Write("\n");

            // Allow time for the device to process
            Thread.Sleep(500);

            // Check for response
            if (serialPort.BytesToRead > 0)
            {
                string response = serialPort.ReadExisting();
                Console.WriteLine($"Response from ESP32: {response}");
            }
        }
        finally
        {
            // Close the port
            if (serialPort.IsOpen)
            {
                serialPort.Close();
                Console.WriteLine($"Disconnected from {portName}");
            }
        }
    }
}

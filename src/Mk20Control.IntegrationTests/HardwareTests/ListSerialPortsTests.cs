using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Lists available serial ports - no device connection required. Formerly
/// <c>Mk20Control.App</c> menu option 1.
/// </summary>
public class ListSerialPortsTests
{
    [Test]
    public void ListSerialPorts_PrintsAvailablePorts()
    {
        var ports = System.IO.Ports.SerialPort.GetPortNames();
        TestContext.WriteLine(ports.Length == 0
            ? "No serial ports found."
            : "Available ports: " + string.Join(", ", ports));
        TestContext.WriteLine("MK20 typically enumerates as a USB CDC-ACM device (USB VID:PID 1d6b:0104 or 1234:5678).");
    }
}

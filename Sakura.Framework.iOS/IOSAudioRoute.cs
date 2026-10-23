// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using AVFoundation;
using Sakura.Framework.Audio;

namespace Sakura.Framework.iOS;

/// <summary>
/// The audio session's current route: the system's I/O buffer and the route itself, a few milliseconds through the
/// speaker and a hundred or more over Bluetooth. Through SDL these come after SDL's own buffers, and its output is one
/// stand-in device whatever the sound plays through. Through RemoteIO the engine fills the I/O buffer itself, so this
/// is all of the way.
/// </summary>
internal sealed class IOSAudioRoute : ISystemAudioRoute
{
    public string Name
    {
        get
        {
            var outputs = AVAudioSession.SharedInstance().CurrentRoute.Outputs;

            if (outputs.Length == 0)
                return string.Empty;

            var output = outputs[0];
            string product = output.PortName;

            // Named the way Android's outputs are, so the same kind of device reads the same on both.
            return output.PortType switch
            {
                var t when t == AVAudioSession.PortBuiltInSpeaker => "Speaker",
                var t when t == AVAudioSession.PortBuiltInReceiver => "Earpiece",
                var t when t == AVAudioSession.PortHeadphones => "Wired headphones",
                var t when t == AVAudioSession.PortBluetoothA2DP || t == AVAudioSession.PortBluetoothLE || t == AVAudioSession.PortBluetoothHfp
                    => $"Bluetooth: {product}",
                var t when t == AVAudioSession.PortUsbAudio => $"USB: {product}",
                var t when t == AVAudioSession.PortHdmi => $"HDMI: {product}",
                var t when t == AVAudioSession.PortAirPlay => $"AirPlay: {product}",
                _ => product
            };
        }
    }

    public double LatencyMilliseconds
    {
        get
        {
            var session = AVAudioSession.SharedInstance();
            return (session.IOBufferDuration + session.OutputLatency) * 1000;
        }
    }
}

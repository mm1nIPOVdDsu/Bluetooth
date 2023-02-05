using System;
using System.Text;
using System.Threading.Tasks;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

using Shared;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Phonebook
{
    internal class Program
    {
        private const string PBAP_UUID = "0000112f-0000-1000-8000-00805f9b34fb";
        private static string deviceId = "YOUR PHONE'S ID GOES HERE ONCE PAIRED";

        static async Task Main(string[] args)
        {
            //await TestWritingStuff();

            BluetoothDevice? bluetoothDevice;
#if DEBUG
            if (string.IsNullOrEmpty(deviceId) is false)
            {
                bluetoothDevice = await BluetoothDevice.FromIdAsync("[YOUR PHONE'S ID GOES HERE ONCE PAIRED]");
            }
            else
            {
                // use Windows.Devices.Enumeration.DeviceWatcher for background discovery
                // true -> paired devices; false -> unpaired devices
                var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(false));
                if (devices.Count is 0)
                    return;

                // do something to select the device to connect with
                var selectedDevice = devices[0];
                bluetoothDevice = await BluetoothDevice.FromIdAsync(selectedDevice.Id);
            }
#else

#endif
            if (bluetoothDevice is null)
                throw new NullReferenceException(nameof(bluetoothDevice));

            // pair if available
            if (bluetoothDevice.DeviceInformation.Pairing.IsPaired is false && bluetoothDevice.DeviceInformation.Pairing.CanPair)
            {
                // a pin will need confirmation on the receiving bluetooth device
                bluetoothDevice.DeviceInformation.Pairing.Custom.PairingRequested += (sender, args) =>
                {
                    var confirmationPin = args.Pin;
                    // show the pin for confirmation
                    args.Accept(confirmationPin);
                };
                var pairResult = await bluetoothDevice.DeviceInformation.Pairing.Custom.PairAsync(DevicePairingKinds.ConfirmPinMatch);
                if (pairResult.Status is not DevicePairingResultStatus.Paired)
                    throw new Exception($"Pairing failed due to {pairResult.Status}.");
            }
            if (bluetoothDevice.DeviceInformation.Pairing.IsPaired is false)
            {
                Console.WriteLine("Device is not paired.");
            }

            // get available RFCOMM services
            // 16-bit UUIDs: https://www.bluetooth.com/specifications/assigned-numbers/
            // NOTE: previous pairing is not necessarily required to get available services
            //       using cached mode as the device has been connected with at this point
            //       or previously
            //var services = await bluetoothDevice.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
            //foreach (var service in services.Services)
            //    Console.WriteLine($"Host Name: {service.ConnectionHostName}; Service ID: {service.ServiceId.Uuid}");

            // or select a specific ID
            // 16-bit UUIDs: https://www.bluetooth.com/specifications/assigned-numbers/
            var pbapUuid = new Guid(PBAP_UUID);
            var serviceResult = await bluetoothDevice.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(pbapUuid), BluetoothCacheMode.Cached);

            // if service doesn't exist or can't be connected to
            if (serviceResult.Services.Count == 0)
                return;

            // NOTE: get and parse the SDP record to determine what features are available 
            //       from the service host
            //       - for PBAP, see https://www.bluetooth.com/specifications/specs/phone-book-access-profile-1-2-3/
            //         Section 7.1.2. in the table, see "PbapSupportedFeatures"
            //       - for parsing, see https://www.bluetooth.com/specifications/specs/core-specification-5-3/
            //         Host Part B, Section 2 page 1176. Take note of section 1.5  
            // the request RFCOMM service, PBAP in this case
            if (serviceResult.Error is not BluetoothError.Success)
                Console.WriteLine($"Attempt to connect to the service failed with response {serviceResult.Error}.");
            var rfcommService = serviceResult.Services[0];

            // request access to the service,
            // NOTE: On Android, this will prompt the user, on iOS consent has to be explicitly set
            //       via Settings > Bluetooth > [hostname] > select 'i' > enable "Sync Contacts" &
            //       optionally "Phone Favorites", "Phone Recents", or "All Contacts".
            // NOTE: This invokes Device Consent and must be called on the UI thread. By default,
            //       unpaired devices do not require consent, while paired devices do. FromIdAsync
            //       will only display a consent prompt when called for a paired device.
            //       RequestAccessAsync allows the app to make the access request explicit in the
            //       event where the device may become paired in the future through other uses of
            //       the device.
            //       If connecting on a non-ui thread, connecting the socket below will cause the
            //       Android consent notification to pop or the iOS Bluetooth permissions be 
            //       appear in System > Bluetooth > (i) > Sync.
            if (rfcommService.DeviceAccessInformation.CurrentStatus is not DeviceAccessStatus.Allowed)
            {
                var accessStatus = await rfcommService.RequestAccessAsync();
                if (accessStatus is not DeviceAccessStatus.Allowed)
                    throw new Exception($"Access to service was {accessStatus}.");
            }

            // opens the connection to a device
            // NOTE: get SDP services via "rfcommService.GetSdpRawAttributesAsync()" to determine
            //       the specific features of the service offered by the device. Features of PBAP
            //       can be found on page 51 of the PBAP specification v1.2.3 at
            //       https://www.bluetooth.com/specifications/specs/.
            // NOTE: the SDP protocol can be found in the Bluetooth Core Specification version 5.3
            //       in Volume 3 Part B, Service Discovery Protocol (SDP) Specification (page 1173).
            //       take not of section 1.5.1, Bit and byte ordering conventions.
            //       https://www.bluetooth.com/specifications/specs/core-specification-5-3/
            var socket = new StreamSocket();
            await socket.ConnectAsync(rfcommService.ConnectionHostName, rfcommService.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionWithAuthentication);

            // for reading and writing OBEX messages
            var writer = new DataWriter(socket.OutputStream);
            var reader = new DataReader(socket.InputStream);

            // the service id
            uint ConnectionID = 0;

            try
            {
                // get the buffer and pass it to the writer connected to the device.
                Console.WriteLine("Sending connection request.");
                await WriteConnectionPacket(writer);
                // parse the response
                Console.WriteLine("Reading connection response.");
                ConnectionID = await ReadConnectionPacket(reader);
                // write the pull phone book packet
                Console.WriteLine("Sending pull phone book request.");
                await WritePullPhonebookPacket(writer, ConnectionID);

                // read the pull phone book response packet
                Console.WriteLine("Reading pull phone book response.");
                string contacts = await ReadPullPhonebookPacket(reader, writer);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                if (ConnectionID > 0)
                {
                    // disconnection from service if a connection has been made
                    Console.WriteLine("Disconnecting from service.");
                    await WriteDisconnectPacket(writer, ConnectionID);

                    // give it time to process before closing the socket
                    await Task.Delay(500);
                }

                // dispose of socket
                reader.Dispose();
                writer.Dispose();
                socket.Dispose();
            }
        }

        static async Task TestWritingStuff()
        {
            ushort connectionId = ushort.Parse("49B0", System.Globalization.NumberStyles.AllowHexSpecifier);
            var socket = new StreamSocket();
            var writer = new DataWriter(socket.OutputStream);
            var reader = new DataReader(socket.InputStream);

            // fields used in pull phone book packet
            // NOTE: the order of the fields is expected in a specific order
            //       as listed in PBAP Specification v1.2.3 section 6.4
            byte ConnectOpCode = (byte)OpCode.GetFinal;
            ushort PacketLength;

            // the connection id, name of the folder to get contacts from, and the defined type
            // PBAP Specification v1.2.3 Section 5.1 
            byte ConnectionIDHeaderID = (byte)HeaderId.ConnectionId;
            byte[] ConnectionIDValue = BitConverter.GetBytes(connectionId);

            byte NameHeaderID = (byte)HeaderId.Name;
            byte[] NameValue = Encoding.ASCII.GetBytes("telecom/pb.vcf");

            byte TypeHeaderID = (byte)HeaderId.Type;
            byte[] TypeValue = Encoding.ASCII.GetBytes("x-bt/phonebook");

            // determine the length of all bytes in the packet
            // size of ConnectOpCode, PacketLength, 3 header IDs, and their lengths
            // NOTE: the size of fields/headers can and will vary, the size should be determined at run time.
            PacketLength = (ushort)(sizeof(byte) + sizeof(ushort) + (sizeof(byte) * 3) + (sizeof(ushort) * 2) + ConnectionIDValue.Length + NameValue.Length + TypeValue.Length);

            // create a writer to receive all connection data
            var packetWriter = new DataWriter();
            // write fields
            packetWriter.WriteByte(ConnectOpCode);
            packetWriter.WriteUInt16(PacketLength);

            // write name header
            packetWriter.WriteByte(NameHeaderID);
            packetWriter.WriteUInt16((ushort)(NameValue.Length + sizeof(HeaderId) + sizeof(ushort)));
            packetWriter.WriteBytes(NameValue);
            // write type header
            packetWriter.WriteByte(TypeHeaderID);
            packetWriter.WriteUInt16((ushort)(TypeValue.Length + sizeof(HeaderId) + sizeof(ushort)));
            packetWriter.WriteBytes(TypeValue);
            // write connection id header
            packetWriter.WriteByte(ConnectionIDHeaderID);
            packetWriter.WriteBytes(ConnectionIDValue);

            // send the request packet
            var detachedBuffer = packetWriter.DetachBuffer();
            writer.WriteBuffer(detachedBuffer);
            await writer.StoreAsync();

            // write the packet to the console window
            Console.WriteLine(BitConverter.ToString(detachedBuffer.ToArray()));
        }

        #region Service Connection
        /// <summary>
        ///     Sends a PBAP connection request to a connected device.
        /// </summary>
        /// <remarks>
        ///     <see href="https://www.bluetooth.com/specifications/specs/phone-book-access-profile-1-2-3/">PBAP Specification 1.2.3 Section 6.4</see>.
        /// </remarks>
        /// <param name="writer">The <see cref="DataWriter"/> created from the <see cref="StreamSocket"/> connection.</param>
        /// <returns><see cref="Task"/></returns>
        static async Task WriteConnectionPacket(DataWriter writer)
        {
            // fields used in connection packet
            // NOTE: the order of the fields is expected in a specific order
            //       as listed in PBAP Specification v1.2.3 section 6.4
            byte ConnectOpCode = (byte)OpCode.Connect;
            ushort PacketLength;
            byte ObexVersion = 0x10;
            byte Flags = 0x00;
            ushort MaximumPacketLength = 0x0FA0; // GOEP v2.1.1 section 7.1.1

            // The use of the Target header is mandatory in the Phone Book Access Profile. 
            // PBAP Specification v1.2.3 Section 6.4 
            byte TargetHeaderID = (byte)HeaderId.Target;
            // this is just the GUID "796135f0-f0c5-11d8-0966-0800200c9a66" broken up into an array of bytes
            byte[] TargetValue = new byte[] { 0x79, 0x61, 0x35, 0xf0, 0xf0, 0xc5, 0x11, 0xd8, 0x09, 0x66, 0x08, 0x00, 0x20, 0x0c, 0x9a, 0x66 };

            // determine the length of all bytes in the packet size of ConnectOpCode, PacketLength, ObexVersion,
            // Flags, MaximumPacketLength, TargetHeaderID, and TargetValue + extra 2 bytes for the header size.
            // NOTE: the breaking out each packet size is for visualization purposes only. the size of
            //       fields/headers can and will vary, and should be determined at run time.
            PacketLength = (ushort)(sizeof(byte) + sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(ushort) + sizeof(byte) + TargetValue.Length + sizeof(short));

            // create a writer to receive all connection data
            var packetWriter = new DataWriter();
            // write fields
            packetWriter.WriteByte(ConnectOpCode);
            packetWriter.WriteUInt16(PacketLength);
            packetWriter.WriteByte(ObexVersion);
            packetWriter.WriteByte(Flags);
            packetWriter.WriteUInt16(MaximumPacketLength);

            // write target header
            packetWriter.WriteByte(TargetHeaderID);
            // writes the size of the TargetValue header: header id, header length, length of the size bytes
            // NOTE: this is only when the size of the header can vary. if the header length is defined
            //       in specification, just write the header bytes.
            packetWriter.WriteUInt16((ushort)(TargetValue.Length + sizeof(HeaderId) + sizeof(ushort)));
            packetWriter.WriteBytes(TargetValue);

            // send the connect packet
            var detachedBuffer = packetWriter.DetachBuffer();
            writer.WriteBuffer(detachedBuffer);
            await writer.StoreAsync();

            //// write the packet to the console window
            //Console.WriteLine(BitConverter.ToString(detachedBuffer.ToArray()));
        }
        /// <summary>
        ///     Reads a PBAP connection response from a connected device.
        /// </summary>
        /// <remarks>
        ///     <see href="https://www.bluetooth.com/specifications/specs/phone-book-access-profile-1-2-3/">PBAP Specification 1.2.3 Section 6.4</see>.
        /// </remarks>
        /// <param name="reader">The <see cref="DataReader"/> created from the <see cref="StreamSocket"/> connection.</param>
        /// <returns>The <see cref="ushort"/> connection ID.</returns>
        /// <exception cref="Exception">
        ///     Thrown when the <see cref="DataReader"/> has nothing to read. Typically occurs when a malformed packet is sent to the connected service.
        /// </exception>
        static async Task<uint> ReadConnectionPacket(DataReader reader)
        {
            // read the message
            if (await reader.LoadAsync(1) != 1)
                throw new Exception("Could not read data from the connect data stream.");

            // first byte is OpCode
            byte ConnectResponseCode = reader.ReadByte();
            Console.WriteLine($"Response Code: {BitConverter.ToString(new byte[1] { ConnectResponseCode })}");
            if (ConnectResponseCode != (byte)ResponseOpCode.Success)
                throw new Exception($"OBEX server replied with the error id {ConnectResponseCode}.");

            // next 2 bytes are packet length
            await reader.LoadAsync(sizeof(ushort));
            ushort ResponsePacketLength = reader.ReadUInt16();
            Console.WriteLine($"Packet Length: {BitConverter.ToString(BitConverter.GetBytes(ResponsePacketLength))}");

            // next byte is OBEX version
            await reader.LoadAsync(1);
            byte ResponseObexVersion = reader.ReadByte();
            Console.WriteLine($"OBEX Version: {BitConverter.ToString(new byte[1] { ResponseObexVersion })}");

            // next byte is flags
            await reader.LoadAsync(1);
            byte ResponseFlags = reader.ReadByte();
            Console.WriteLine($"Flags: {BitConverter.ToString(new byte[1] { ResponseFlags })}");

            // next 2 bytes are maximum packet length
            await reader.LoadAsync(sizeof(ushort));
            // change to little Endian
            ushort ResponseMaximumPacketLength = reader.ReadUInt16();
            Console.WriteLine($"Max Packet Length: {BitConverter.ToString(BitConverter.GetBytes(ResponseMaximumPacketLength))}");

            // remove the size of the fields from the total size to determine the size of headers
            // size of ConnectOpCode, PacketLength, ObexVersion, Flags, MaximumPacketLength, and TargetHeaderID
            // NOTE: the breaking out each packet size is for visualization purposes only. the size of
            //       fields/headers can and will vary, and should be determined at run time.
            uint allHeaderSize = (uint)(ResponsePacketLength - (sizeof(byte) + sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(ushort)));

            // load the remaining bytes
            var loaded = await reader.LoadAsync(allHeaderSize);
            if (loaded == 0)
                throw new Exception("Could not read data from the connect data stream.");

            uint connectionId = 0;
            while (reader.UnconsumedBufferLength > 0)
            {
                // get the id of the header
                byte headerId = reader.ReadByte();
                Console.WriteLine($"Header ID: {BitConverter.ToString(new byte[1] { headerId })}");

                // makes big assumption, but this is just an example
                HeaderId header = (HeaderId)headerId;
                if (header == HeaderId.ConnectionId)
                {
                    // connection id is fixed in length
                    connectionId = reader.ReadUInt32();
                    continue;
                }
                ushort headerSize = reader.ReadUInt16();
                Console.WriteLine($"Header Size: {BitConverter.ToString(BitConverter.GetBytes(headerSize))}");

                // get the length of the header byte subtracting the size of the header id and the end of line characters(?)
                ushort headerLength = (ushort)(headerSize - sizeof(byte) - sizeof(ushort));

                // read the remaining bytes from the header
                byte[] headerBytes = new byte[headerLength];
                reader.ReadBytes(headerBytes);
            }

            return connectionId;
        }
        /// <summary>
        ///     Sends a PBAP disconnect request to a connected device.
        /// </summary>
        /// <remarks>
        ///     <see href="https://www.bluetooth.com/specifications/specs/phone-book-access-profile-1-2-3/">PBAP Specification 1.2.3 Section 6.4</see>.
        /// </remarks>
        /// <param name="writer">The <see cref="DataWriter"/> created from the <see cref="StreamSocket"/> connection.</param>
        /// <param name="connectionId">The connection id obtained from <see cref="ReadConnectionPacket(DataReader)"/>.</param>
        /// <returns><see cref="Task"/></returns>
        static async Task WriteDisconnectPacket(DataWriter writer, uint connectionId)
        {
            byte ConnectOpCode = (byte)OpCode.Disconnect;
            ushort PacketLength;
            byte ConnectionIDHeaderID = (byte)HeaderId.ConnectionId;
            byte[] ConnectionIDValue = BitConverter.GetBytes(connectionId);

            // opcode, packet length, and connection id
            PacketLength = sizeof(byte) + sizeof(ushort) + sizeof(ushort);

            // create a writer to receive all packet data
            var packetWriter = new DataWriter();
            // write fields
            packetWriter.WriteByte(ConnectOpCode);
            packetWriter.WriteUInt16(PacketLength);

            // write header
            packetWriter.WriteByte(ConnectionIDHeaderID);
            packetWriter.WriteBytes(ConnectionIDValue);

            // send the request packet
            var detachedBuffer = packetWriter.DetachBuffer();
            writer.WriteBuffer(detachedBuffer);
            await writer.StoreAsync();

            //// write the packet to the console window
            //Console.WriteLine(BitConverter.ToString(detachedBuffer.ToArray()));
        }
        #endregion Service Connection

        #region Pull Contacts
        /// <summary>
        ///     Sends a PBAP pull phone book request to a connected device.
        /// </summary>36
        /// <remarks>
        ///     <see href="https://www.bluetooth.com/specifications/specs/phone-book-access-profile-1-2-3/">PBAP Specification 1.2.3 Section 5.1</see>.
        /// </remarks>
        /// <param name="writer">The <see cref="DataWriter"/> created from the <see cref="StreamSocket"/> connection.</param>
        /// <param name="connectionId">The connection id obtained from <see cref="ReadConnectionPacket(DataReader)"/>.</param>
        /// <returns><see cref="Task"/></returns>
        static async Task WritePullPhonebookPacket(DataWriter writer, uint connectionId)
        {
            // fields used in pull phone book packet
            // NOTE: the order of the fields is expected in a specific order
            //       as listed in PBAP Specification v1.2.3 section 6.4
            byte ConnectOpCode = (byte)OpCode.GetFinal;
            ushort PacketLength;

            // the connection id, name of the folder to get contacts from, and the defined type
            // PBAP Specification v1.2.3 Section 5.1 
            byte ConnectionIDHeaderID = (byte)HeaderId.ConnectionId;
            byte[] ConnectionIDValue = BitConverter.GetBytes(connectionId);

            // NOTE: the "folder" defines specific types of data to be retrieved from the phone book. 
            //       pb -> phone book; ich -> incoming call history; och -> outgoing call history;
            //       mch -> missed call history; cch -> combined call history; spd -> speed dial;
            //       fav -> favorites
            // NOTE: pb.vcf includes call history as well and can be very large

            // the name header
            byte NameHeaderID = (byte)HeaderId.Name; // name header is in Unicode formatting
            string NameString = "telecom/ich.vcf";
            byte[] NameStringBytes = new byte[Encoding.Unicode.GetByteCount(NameString) + 2]; // plus \0 null terminator
            Encoding.BigEndianUnicode.GetBytes(NameString, 0, NameString.Length, NameStringBytes, 0);
            NameStringBytes[^1] = 0; // null terminator
            NameStringBytes[^2] = 0; // null terminator
            // the type header
            byte TypeHeaderID = (byte)HeaderId.Type; // type header is in ASCII formatting
            string TypeString = "x-bt/phonebook";
            byte[] TypeStringBytes = new byte[Encoding.ASCII.GetByteCount(TypeString) + 1]; // plus \0 null terminator
            Encoding.ASCII.GetBytes(TypeString, 0, TypeString.Length, TypeStringBytes, 0);
            TypeStringBytes[^1] = 0; // null terminator

            // the application parameters header. this header is not indicated as required but fails without it.
            byte AppParamHeaderId = (byte)HeaderId.ApplicationParameters;
            byte[] AppParamBytes = Array.Empty<byte>();

            // determine the length of all bytes in the packet
            // size of ConnectOpCode, PacketLength, 3 header IDs, and their lengths
            // NOTE: the breaking out each packet size is for visualization purposes only. the size of
            //       fields/headers can and will vary, and should be determined at run time.
            PacketLength = (ushort)(sizeof(byte) + sizeof(ushort) + (sizeof(byte) * 4) + (sizeof(ushort) * 3) + ConnectionIDValue.Length + NameStringBytes.Length + TypeStringBytes.Length);

            // create a writer to receive all packet data
            var packetWriter = new DataWriter();
            // write fields
            packetWriter.WriteByte(ConnectOpCode);
            packetWriter.WriteUInt16(PacketLength);
            // name header 
            packetWriter.WriteByte(NameHeaderID);
            packetWriter.WriteUInt16((ushort)(NameStringBytes.Length + sizeof(HeaderId) + sizeof(ushort)));
            packetWriter.WriteBytes(NameStringBytes);
            // type header
            packetWriter.WriteByte(TypeHeaderID);
            packetWriter.WriteUInt16((ushort)(TypeStringBytes.Length + sizeof(HeaderId) + sizeof(ushort)));
            packetWriter.WriteBytes(TypeStringBytes);
            // application parameters header
            packetWriter.WriteByte(AppParamHeaderId);
            packetWriter.WriteUInt16((ushort)(AppParamBytes.Length + sizeof(HeaderId) + sizeof(ushort)));
            packetWriter.WriteBytes(AppParamBytes);
            // connection id header
            packetWriter.WriteByte(ConnectionIDHeaderID);
            packetWriter.WriteBytes(ConnectionIDValue);

            // send the request packet
            var detachedBuffer = packetWriter.DetachBuffer();
            writer.WriteBuffer(detachedBuffer);
            await writer.StoreAsync();

            //// write the packet to the console window
            //Console.WriteLine(BitConverter.ToString(detachedBuffer.ToArray()));
        }
        /// <summary>
        ///     Reads a PBAP pull phone book response from a connected device.
        /// </summary>
        /// <remarks>
        ///     <see href="https://www.bluetooth.com/specifications/specs/phone-book-access-profile-1-2-3/">PBAP Specification 1.2.3 Section 5.1</see>.
        /// </remarks>
        /// <param name="reader">The <see cref="DataReader"/> created from the <see cref="StreamSocket"/> connection.</param>
        /// <param name="writer">The <see cref="DataWriter"/> created from the <see cref="StreamSocket"/> connection.</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        static async Task<string> ReadPullPhonebookPacket(DataReader reader, DataWriter writer)
        {
            // read the message
            if (await reader.LoadAsync(1) != 1)
                throw new Exception("Could not read data from the connect data stream.");

            byte ConnectResponseCode = reader.ReadByte();
            if (ConnectResponseCode is not (byte)ResponseOpCode.Continue and not (byte)ResponseOpCode.Success)
                throw new Exception($"OBEX server replied with the error id {ConnectResponseCode}.");

            // next 2 bytes are packet length
            await reader.LoadAsync(sizeof(ushort));
            ushort ResponsePacketLength = reader.ReadUInt16();
            //Console.WriteLine($"Packet Length: {BitConverter.ToString(BitConverter.GetBytes(ResponsePacketLength))}");

            // remove the size of the fields from the total size to determine the size of headers
            // size of ConnectOpCode, PacketLength, ObexVersion, Flags, MaximumPacketLength, and TargetHeaderID
            uint allHeaderSize = (uint)(ResponsePacketLength - (sizeof(byte) + sizeof(ushort)));

            // load the remaining bytes
            var loaded = await reader.LoadAsync(allHeaderSize);
            if (loaded == 0)
                throw new Exception("Could not read data from the connect data stream.");

            // NOTE: Bluetooth OBEX has a maximum packet size of 0xFFFF so data will be sent in chunks
            //       of up to 0xFFFF. to retrieve data larger than this, multiple requests must be 
            //       sent to the connected device. the device will return an OpCode of "Continue"
            //       indicating more data is available. once all data is retrieved, the connected
            //       device will return an OpCode of "Success" or an error code.
            string contacts = string.Empty;
            uint connectionId = 0;
            while (reader.UnconsumedBufferLength > 0)
            {
                // get the id of the header
                byte headerId = reader.ReadByte();
                Console.WriteLine($"Header ID: {BitConverter.ToString(new byte[1] { headerId })}");

                // makes big assumption, but this is just an example
                HeaderId header = (HeaderId)headerId;
                if (header == HeaderId.ConnectionId)
                {
                    // connection id is fixed in length
                    connectionId = reader.ReadUInt32();
                    continue;
                }
                ushort headerSize = reader.ReadUInt16();
                //Console.WriteLine($"Header Size: {BitConverter.ToString(BitConverter.GetBytes(headerSize))}");

                // get the length of the header byte subtracting the size of the header id and the end of line characters(?)
                ushort headerLength = (ushort)(headerSize - sizeof(byte) - sizeof(ushort));

                // read the remaining bytes from the header
                byte[] headerBytes = new byte[headerLength];
                reader.ReadBytes(headerBytes);

                // TODO: parse the data in the header
                //       for connection, this will be the Who header and Connection ID header unless authentication is used.
                // the server will return an OpCode of continue until everything has been transferred. 
                if (header is HeaderId.EndOfBody || header is HeaderId.Body)
                {
                    contacts = Encoding.UTF8.GetString(headerBytes, 0, headerBytes.Length);
                }
            }

            if (ConnectResponseCode is (byte)ResponseOpCode.Continue && connectionId is not 0)
            {
                await WritePullPhonebookPacket(writer, connectionId);
                contacts += await ReadPullPhonebookPacket(reader, writer);
            }

            return contacts;
        }
        #endregion Pull Contacts
    }
}
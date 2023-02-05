namespace Shared
{
    /// <summary>
    ///     Methods carried out in an OBEX session.
    /// </summary>
    public enum OpCode : byte
    {
        /// <summary>
        ///     Connects to the Server using the CONNECT operation of the OBEX protocol.
        /// </summary>
        /// <remarks>
        ///     <see cref="https://www.bluetooth.com/specifications/specs/generic-object-exchange-profile-2-1-1/">GOEP v2.1.1 Section 5.4.1</see>
        /// </remarks>
        Connect = 0x80,
        /// <summary>
        ///     Disconnects from the Server using the DISCONNECT operation of the OBEX protocol.
        /// </summary>
        /// <remarks>
        ///     <see cref="https://www.bluetooth.com/specifications/specs/generic-object-exchange-profile-2-1-1/">GOEP v2.1.1 Section 5.1.2</see>
        /// </remarks>
        Disconnect = 0x81,
        /// <summary>
        ///     Pulls from the Server using the GET operation of the OBEX protocol.
        /// </summary>
        /// <remarks>
        ///     <see cref="https://www.bluetooth.com/specifications/specs/generic-object-exchange-profile-2-1-1/">GOEP v2.1.1 Section 5.6.1</see>
        /// </remarks>
        Get = 0x03,
        /// <summary>
        ///     Pulls from the Server using the GET operation of the OBEX protocol.
        /// </summary>
        /// <remarks>
        ///     The Final Bit (0x83) can be used only when all of the 
        ///     request headers have been issued and shall be issued 
        ///     in all subsequent request packets until the operation 
        ///     is completed.
        ///     
        ///     <see cref="https://www.bluetooth.com/specifications/specs/generic-object-exchange-profile-2-1-1/">GOEP v2.1.1 Section 5.6.1</see>
        /// </remarks>
        GetFinal = 0x83,
    }
}

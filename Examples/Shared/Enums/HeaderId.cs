namespace Shared
{
    /// <summary>
    ///     Denotes the type header type in an OBEX packet.
    /// </summary>
    public enum HeaderId : byte
    {
        /// <summary>
        ///     Extended application request and response information.
        /// </summary>
        ApplicationParameters = 0x4C,
        /// <summary>
        ///     Authentication digest-response.
        /// </summary>
        AuthResponse = 0x4E,
        /// <summary>
        ///     A chunk of the object body.
        /// </summary>
        Body = 0x48,
        /// <summary>
        ///     An identifier used for OBEX connection multiplexing.
        /// </summary>
        ConnectionId = 0xCB,
        /// <summary>
        ///     The final chunk of the object body.
        /// </summary>
        EndOfBody = 0x49,
        /// <summary>
        ///     Name of the object (often a file name).
        /// </summary>
        Name = 0x01,
        /// <summary>
        ///     Name of service that operation is targeted to.
        /// </summary>
        Target = 0x46,
        /// <summary>
        ///     Type of object - e.g. text, HTML, binary, manufacturer specific.
        /// </summary>
        Type = 0x42,
        /// <summary>
        ///     Identifies the OBEX application, used to tell if talking to a peer.
        /// </summary>
        Who = 0x4A,
    }
}

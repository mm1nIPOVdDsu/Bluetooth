namespace Shared
{
    /// <summary>
    ///     Response codes of an OBEX message request.
    /// </summary>
    public enum ResponseOpCode : byte
    {
        /// <summary>
        ///     Continue, more information to receive.
        /// </summary>
        Continue = 0x90,
        /// <summary>
        ///     The request was successful.
        /// </summary>
        Success = 0xA0,
    }
}

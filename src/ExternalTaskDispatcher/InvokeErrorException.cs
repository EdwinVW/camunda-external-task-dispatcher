using System;
using System.Runtime.Serialization;

namespace ExternalTaskDispatcher
{
    public class InvokeErrorException : Exception
    {
        public InvokeErrorException()
        {
        }

        public InvokeErrorException(string message) : base(message)
        {
        }

        public InvokeErrorException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected InvokeErrorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
using System;

namespace NokitaKaze.WAVParser
{
    public class ParsingException : Exception
    {
        public ParsingException() : base()
        {
        }

        public ParsingException(string message) : base(message)
        {
        }
    }
}
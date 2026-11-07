using System;

namespace LanShare.Https
{
    /// <summary>Un elemento DER ya delimitado: su etiqueta y donde empieza y acaba su valor.</summary>
    public sealed class DerElement
    {
        private readonly byte[] _source;

        public DerElement(byte tag, byte[] source, int contentOffset, int contentLength)
        {
            Tag = tag;
            _source = source;
            ContentOffset = contentOffset;
            ContentLength = contentLength;
        }

        public byte Tag { get; private set; }
        public int ContentOffset { get; private set; }
        public int ContentLength { get; private set; }

        /// <summary>Copia del valor, sin etiqueta ni longitud.</summary>
        public byte[] Content()
        {
            byte[] content = new byte[ContentLength];
            Buffer.BlockCopy(_source, ContentOffset, content, 0, ContentLength);
            return content;
        }

        /// <summary>Lector sobre el valor, para recorrer lo que contiene una SEQUENCE.</summary>
        public DerReader Children()
        {
            return new DerReader(_source, ContentOffset, ContentLength);
        }
    }

    /// <summary>
    /// Lector DER minimo para leer de vuelta las extensiones de los
    /// certificados ya emitidos (los nombres que cubre uno, la clave de quien
    /// lo firmo). Comprueba cada longitud contra lo que queda: unos bytes mal
    /// formados dan FormatException, nunca una lectura fuera del arreglo.
    /// </summary>
    public sealed class DerReader
    {
        /// <summary>Longitudes de hasta 4 bytes: mas que suficiente para un certificado.</summary>
        private const int MaxLengthBytes = 4;

        private readonly byte[] _data;
        private readonly int _end;
        private int _position;

        public DerReader(byte[] data)
            : this(data, 0, data == null ? 0 : data.Length)
        {
        }

        public DerReader(byte[] data, int offset, int length)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (offset < 0 || length < 0 || offset > data.Length - length)
            {
                throw new ArgumentOutOfRangeException("length");
            }

            _data = data;
            _position = offset;
            _end = offset + length;
        }

        public bool HasMore
        {
            get { return _position < _end; }
        }

        public DerElement Read()
        {
            if (_position >= _end) throw new FormatException("DER: se esperaba otro elemento.");

            byte tag = _data[_position++];

            // Etiquetas de varios bytes (numero 31 o mayor): no aparecen en lo
            // que se lee aqui, y aceptarlas sin entenderlas seria leer mal.
            if ((tag & 0x1F) == 0x1F) throw new FormatException("DER: etiqueta larga no admitida.");

            int length = ReadLength();
            if (length > _end - _position) throw new FormatException("DER: la longitud se sale del bloque.");

            DerElement element = new DerElement(tag, _data, _position, length);
            _position += length;
            return element;
        }

        /// <summary>Lee un elemento y exige que sea de la etiqueta esperada.</summary>
        public DerElement Read(byte expectedTag)
        {
            DerElement element = Read();
            if (element.Tag != expectedTag)
            {
                throw new FormatException(string.Format("DER: se esperaba la etiqueta 0x{0:X2} y llego 0x{1:X2}.",
                                                        expectedTag, element.Tag));
            }
            return element;
        }

        private int ReadLength()
        {
            if (_position >= _end) throw new FormatException("DER: falta la longitud.");

            int first = _data[_position++];
            if (first < 0x80) return first;

            // 0x80 a secas es la longitud indefinida de BER: DER no la admite.
            int byteCount = first & 0x7F;
            if (byteCount == 0 || byteCount > MaxLengthBytes)
            {
                throw new FormatException("DER: longitud indefinida o demasiado grande.");
            }
            if (byteCount > _end - _position) throw new FormatException("DER: longitud cortada.");

            long length = 0;
            for (int i = 0; i < byteCount; i++) length = (length << 8) | _data[_position++];

            if (length > int.MaxValue) throw new FormatException("DER: longitud fuera de rango.");
            return (int)length;
        }
    }
}

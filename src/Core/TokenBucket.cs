using System;

namespace LanShare.Core
{
    /// <summary>
    /// Limitador de ritmo por cubeta de fichas: deja pasar rafagas de hasta
    /// <see cref="Capacity"/> y, de forma sostenida, tantas por segundo como
    /// se indique. Cada concesion gasta una ficha; las fichas se reponen con
    /// el tiempo hasta llenar la cubeta.
    ///
    /// El tiempo lo aporta quien llama, en milisegundos de un reloj monotono:
    /// asi se puede probar sin esperar y sin depender del reloj del sistema,
    /// que puede saltar hacia atras.
    /// </summary>
    public sealed class TokenBucket
    {
        private readonly object _gate = new object();
        private readonly int _capacity;
        private readonly double _refillPerMs;
        private double _tokens;
        private long _lastMs;

        public TokenBucket(int capacity, double refillPerSecond, long nowMs)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity");
            if (!(refillPerSecond > 0)) throw new ArgumentOutOfRangeException("refillPerSecond");

            _capacity = capacity;
            _refillPerMs = refillPerSecond / 1000.0;
            _tokens = capacity;
            _lastMs = nowMs;
        }

        public int Capacity
        {
            get { return _capacity; }
        }

        /// <summary>Cuantas de las <paramref name="wanted"/> fichas pedidas se conceden.</summary>
        public int Take(int wanted, long nowMs)
        {
            if (wanted <= 0) return 0;

            lock (_gate)
            {
                // Un reloj que retrocede no repone ni mueve la referencia.
                if (nowMs > _lastMs)
                {
                    _tokens = Math.Min(_capacity, _tokens + (nowMs - _lastMs) * _refillPerMs);
                    _lastMs = nowMs;
                }

                int granted = (int)Math.Min(wanted, Math.Floor(_tokens));
                _tokens -= granted;
                return granted;
            }
        }
    }
}

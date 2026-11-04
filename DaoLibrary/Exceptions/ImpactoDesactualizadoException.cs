using EntityLibrary;

namespace DaoLibrary.Exceptions
{
    // HU-106 (409): al confirmar un cambio de arancel, el impacto recalculado ya no es el de la simulación que se
    // aprobó (cambiaron aranceles, jugadores o cuotas en el medio). No se grabó nada; ImpactoActual es el nuevo resumen
    // para revisar y volver a confirmar.
    public class ImpactoDesactualizadoException : Exception
    {
        public ImpactoArancel ImpactoActual { get; }

        public ImpactoDesactualizadoException(ImpactoArancel impactoActual)
            : base("El impacto del cambio es distinto del que revisaste (cambiaron aranceles, jugadores o cuotas mientras tanto). No se guardó nada: revisá el nuevo resumen y volvé a confirmar.")
        {
            ImpactoActual = impactoActual;
        }
    }
}

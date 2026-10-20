// Pluralização correta em português (sem "(s)").
namespace NovoFtool
{
    public static class Pl
    {
        /// <summary>"1 barra" / "2 barras".</summary>
        public static string N(int n, string sing, string plur) { return n + " " + (n == 1 ? sing : plur); }
    }
}

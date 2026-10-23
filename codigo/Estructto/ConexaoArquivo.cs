// Arquivo de conexão (.pcon): a calculadora de ligação como arquivo próprio, sem modelo (como o Inventor separa peça e montagem).
// Guarda só a entrada da ligação (tipo, dados, esforços); a verificação é refeita pelo motor ao abrir. O modelo usa .pstr
// (o .estructto antigo continua abrindo). Abrir o .pcon dentro do projetista de um grupo preenche aquela ligação do modelo;
// "Aplicar ao grupo" grava (JanelaLigacaoProjeto.AbrirConexao/SalvarConexao).
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovoFtool
{
    public static class ConexaoArquivo
    {
        public const string Ext = ".pcon";
        public const string Formato = "estructto-conexao";
        public const int Versao = 1;

        public static string Serializar(string nome, string tipo, JsonObject dados, JsonObject esforcos)
        {
            var o = new JsonObject
            {
                ["formato"] = Formato, ["versao"] = Versao, ["nome"] = nome ?? "", ["tipo"] = tipo,
                ["dados"] = (dados ?? new JsonObject()).DeepClone(), ["esforcos"] = (esforcos ?? new JsonObject()).DeepClone(),
            };
            return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        /// <summary>Lê o texto de um .pcon. FormatException com a mensagem para o usuário se não for um arquivo de conexão válido.</summary>
        public static (string nome, string tipo, JsonObject dados, JsonObject esforcos) Ler(string texto)
        {
            JsonObject o;
            try { o = JsonNode.Parse(texto) as JsonObject; }
            catch (JsonException ex) { throw new FormatException("JSON inválido: " + ex.Message); }
            if (o == null || (string)o["formato"] != Formato) throw new FormatException("não é um arquivo de conexão do Estructto (" + Ext + ").");
            int v = o["versao"] is JsonValue jv && jv.TryGetValue(out int n) ? n : 0;
            if (v < 1 || v > Versao) throw new FormatException("versão " + v + " não suportada (este Estructto lê até a " + Versao + ").");
            string tipo = (string)o["tipo"];
            if (string.IsNullOrWhiteSpace(tipo)) throw new FormatException("o arquivo não diz o tipo da ligação.");
            if (!(o["dados"] is JsonObject d)) throw new FormatException("campo 'dados' ausente ou inválido.");
            if (!(o["esforcos"] is JsonObject e)) throw new FormatException("campo 'esforcos' ausente ou inválido.");
            return ((string)o["nome"] ?? "", tipo, d, e);
        }

        public static void Gravar(string caminho, string nome, string tipo, JsonObject dados, JsonObject esforcos)
            => Config.GravarAtomico(caminho, Serializar(nome, tipo, dados, esforcos));

        public static bool EhConexao(string caminho) => string.Equals(Path.GetExtension(caminho ?? ""), Ext, StringComparison.OrdinalIgnoreCase);
    }
}

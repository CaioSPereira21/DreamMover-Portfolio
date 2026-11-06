using DreamMover.DTO;
using DreamMover.Helpers;
using DreamMover.Models;
using DreamMover.Services;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Core.Metadata.Edm;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Web;
using System.Web.Mvc;

namespace DreamMover.Controllers
{
    public partial class ConsLutadorController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult DetalhesLutador(int id)
        {
            var lutador = _db.Lutador.Where(x => x.Id == id).ToList()
                .Select(x => LutadorDTO.ToDTO(x)).FirstOrDefault();

            if (lutador == null)
                return HttpNotFound();

            PreencherDetalhesLutador(lutador);
            ViewBag.Lutador = lutador;

            return PartialView("~/Views/DetalhesLutador/Index.cshtml");
        }

        private lutasEntities _db;

        public ConsLutadorController()
        {
            _db = new lutasEntities();
        }

        [HttpPost]
        public JsonResult GetLutadores()
        {
            var partidasComoCasa = _db.Partida
                .Where(x => x.IdLutadorCasa.HasValue)
                .GroupBy(x => x.IdLutadorCasa.Value)
                .Select(grupo => new { IdLutador = grupo.Key, Quantidade = grupo.Count() })
                .ToDictionary(x => x.IdLutador, x => x.Quantidade);

            var partidasComoVisitante = _db.Partida
                .Where(x => x.IdLutadorVisitante.HasValue)
                .GroupBy(x => x.IdLutadorVisitante.Value)
                .Select(grupo => new { IdLutador = grupo.Key, Quantidade = grupo.Count() })
                .ToDictionary(x => x.IdLutador, x => x.Quantidade);

            var lutadores = _db.Lutador
                .Include(x => x.Tag)
                .ToList()
                .Select(x => LutadorDTO.ToDTO(x))
                .OrderBy(x => x.Nome)
                .ToList();

            foreach (var lutador in lutadores)
            {
                int partidasCasa;
                int partidasVisitante;
                partidasComoCasa.TryGetValue(lutador.Id, out partidasCasa);
                partidasComoVisitante.TryGetValue(lutador.Id, out partidasVisitante);
                lutador.Partidas = partidasCasa + partidasVisitante;
            }

            DivisionService.PreencherLutadores(_db, lutadores);

            return Json(new { sucesso = true, lutadores }, JsonRequestBehavior.DenyGet);
        }

        [HttpPost]
        public JsonResult GetTags()
        {
            var tags = _db.Tag
                .OrderBy(x => x.Nome)
                .Select(x => new TagDTO { Id = x.Id, Nome = x.Nome })
                .ToList();

            return Json(new { sucesso = true, tags }, JsonRequestBehavior.DenyGet);
        }

        [HttpPost]
        public JsonResult VerificarAutorNaPasta(int id)
        {
            var lutador = _db.Lutador.FirstOrDefault(x => x.Id == id);
            if (lutador == null)
                return Json(new { sucesso = false, mensagem = "Lutador nao encontrado." }, JsonRequestBehavior.DenyGet);

            try
            {
                string charsPath = ObterCaminhoCharsConfigurado();
                var personagem = LocalizarPersonagemChar(charsPath, lutador);

                if (personagem == null)
                {
                    return Json(new
                    {
                        sucesso = true,
                        encontrado = false,
                        mensagem = "Nao foi encontrada uma pasta correspondente a este personagem."
                    }, JsonRequestBehavior.DenyGet);
                }

                if (!string.Equals(lutador.PastaPersonagem, personagem.Pasta, StringComparison.OrdinalIgnoreCase))
                {
                    lutador.PastaPersonagem = personagem.Pasta;
                    _db.SaveChanges();
                }

                string autorCadastrado = (lutador.Autor ?? string.Empty).Trim();
                string autorDetectado = (personagem.Autor ?? string.Empty).Trim();
                bool autorInformadoNaPasta = !string.IsNullOrWhiteSpace(autorDetectado);
                bool autorAlterado = autorInformadoNaPasta
                    && !string.Equals(autorCadastrado, autorDetectado, StringComparison.OrdinalIgnoreCase);
                MugenCharacterSecurityInspection segurancaPersonagem =
                    MugenCharacterSecurityService.Inspect(personagem.DefPath);

                return Json(new
                {
                    sucesso = true,
                    encontrado = true,
                    pasta = personagem.Pasta,
                    arquivoDef = Path.GetFileName(personagem.DefPath),
                    autorCadastrado,
                    autorDetectado,
                    autorInformadoNaPasta,
                    autorAlterado,
                    seguranca = new
                    {
                        sucesso = segurancaPersonagem.Success,
                        arquivoCns = segurancaPersonagem.CnsFile,
                        confiavel = segurancaPersonagem.Reliable,
                        seguro = segurancaPersonagem.Safe,
                        alteradoParaFicarMaisForte = segurancaPersonagem.HasHigherAttribute,
                        possuiValoresInvalidos = segurancaPersonagem.HasInvalidValue,
                        mensagem = segurancaPersonagem.Message,
                        atributos = segurancaPersonagem.Attributes.Select(atributo => new
                        {
                            chave = atributo.Key,
                            nome = atributo.Name,
                            editavel = atributo.Editable,
                            possuiPadrao = atributo.HasStandard,
                            valor = atributo.Value,
                            valorOriginal = atributo.RawValue,
                            valorPadrao = atributo.StandardValue,
                            informado = atributo.Informed,
                            valido = atributo.Valid,
                            acimaDoPadrao = atributo.HigherThanStandard
                        }).ToList()
                    }
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    sucesso = false,
                    mensagem = "Nao foi possivel verificar o personagem na pasta. " + ex.Message
                }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult CadastrarTag(string nome)
        {
            string nomeNormalizado = (nome ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(nomeNormalizado))
                return Json(new { sucesso = false, mensagem = "Informe o nome da tag." }, JsonRequestBehavior.DenyGet);

            if (nomeNormalizado.Length > 100)
                return Json(new { sucesso = false, mensagem = "A tag deve possuir no maximo 100 caracteres." }, JsonRequestBehavior.DenyGet);

            var tagExistente = _db.Tag
                .ToList()
                .FirstOrDefault(x => string.Equals((x.Nome ?? string.Empty).Trim(), nomeNormalizado, StringComparison.OrdinalIgnoreCase));

            if (tagExistente != null)
            {
                return Json(new
                {
                    sucesso = true,
                    existente = true,
                    tag = new TagDTO { Id = tagExistente.Id, Nome = tagExistente.Nome }
                }, JsonRequestBehavior.DenyGet);
            }

            try
            {
                var tag = new Tag { Nome = nomeNormalizado };
                _db.Tag.Add(tag);
                _db.SaveChanges();

                return Json(new
                {
                    sucesso = true,
                    existente = false,
                    tag = new TagDTO { Id = tag.Id, Nome = tag.Nome }
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = "Nao foi possivel cadastrar a tag. " + ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult GetUniversos()
        {
            string imagensPath = Server.MapPath("~/Imagens");
            var universosBanco = _db.Lutador
                .Where(x => !string.IsNullOrEmpty(x.Universo))
                .Select(x => x.Universo)
                .ToList();

            var universosImagens = Directory.Exists(imagensPath)
                ? Directory.GetFiles(imagensPath, "*.png")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(EhImagemDeUniverso)
                    .ToList()
                : new List<string>();

            var universos = universosBanco
                .Concat(universosImagens)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .Select(x => new { nome = x, imagem = "/Imagens/" + x + ".png" })
                .ToList();

            return Json(new { sucesso = true, universos }, JsonRequestBehavior.DenyGet);
        }

        [HttpPost]
        public JsonResult GetPersonagensCharsDisponiveis()
        {
            try
            {
                var personagens = ObterPersonagensCharsDisponiveis();
                var response = Json(new { sucesso = true, personagens }, JsonRequestBehavior.DenyGet);
                response.MaxJsonLength = int.MaxValue;
                return response;
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult CadastrarPersonagensDosChars(string universo, List<string> pastasPersonagem)
        {
            string universoNormalizado = NormalizarNomeUniverso(universo);
            if (string.IsNullOrWhiteSpace(universoNormalizado))
                return Json(new { sucesso = false, mensagem = "Informe o universo dos personagens." }, JsonRequestBehavior.DenyGet);

            string imagemUniverso = Server.MapPath("~/Imagens/" + universoNormalizado + ".png");
            if (!System.IO.File.Exists(imagemUniverso))
                return Json(new { sucesso = false, mensagem = "Imagem do universo nao encontrada em /Imagens/" + universoNormalizado + ".png" }, JsonRequestBehavior.DenyGet);

            var selecionadas = new HashSet<string>(
                (pastasPersonagem ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);

            if (!selecionadas.Any())
                return Json(new { sucesso = false, mensagem = "Selecione pelo menos um personagem." }, JsonRequestBehavior.DenyGet);

            try
            {
                var personagens = ObterPersonagensCharsDisponiveis()
                    .Where(x => selecionadas.Contains(x.Pasta))
                    .ToList();

                if (!personagens.Any())
                    return Json(new { sucesso = false, mensagem = "Nenhum personagem disponivel foi selecionado." }, JsonRequestBehavior.DenyGet);

                var nomesExistentes = new HashSet<string>(
                    _db.Lutador.Select(x => x.Nome).ToList().Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);

                int cadastrados = 0;
                var ignorados = new List<object>();

                foreach (var personagem in personagens)
                {
                    if (string.IsNullOrWhiteSpace(personagem.Nome) || nomesExistentes.Contains(personagem.Nome))
                    {
                        ignorados.Add(new { personagem.Nome, personagem.Pasta, motivo = "Ja cadastrado ou nome invalido." });
                        continue;
                    }

                    var lutador = new Lutador
                    {
                        Nome = personagem.Nome,
                        Autor = personagem.Autor,
                        Universo = universoNormalizado,
                        Stand = personagem.Nome + "Stand.gif",
                        Portrait = personagem.Nome + "Portrait.png",
                        PastaPersonagem = personagem.Pasta,
                        Exp = 0,
                        Nivel = 1,
                        RatingElo = ProgressionCalculator.RatingEloInicial
                    };

                    _db.Lutador.Add(lutador);
                    nomesExistentes.Add(lutador.Nome);
                    cadastrados++;
                }

                if (cadastrados > 0)
                    _db.SaveChanges();

                var response = Json(new { sucesso = true, cadastrados, ignorados }, JsonRequestBehavior.DenyGet);
                response.MaxJsonLength = int.MaxValue;
                return response;
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult IniciarEnvioPacotesChars(string arquivos)
        {
            try
            {
                List<CharacterArchiveFileDescriptor> lista = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CharacterArchiveFileDescriptor>>(arquivos ?? string.Empty);
                string token = CharacterArchiveImportService.IniciarEnvio(lista, ObterPastaTemporariaChars());
                return Json(new { sucesso = true, token }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult EnviarPartePacoteChar(string token, int indice, long offset)
        {
            try
            {
                HttpPostedFileBase parte = Request.Files["parte"];
                if (parte == null)
                    throw new InvalidOperationException("Nenhuma parte de pacote foi recebida.");
                long recebido = CharacterArchiveImportService.ReceberParte(
                    token, indice, offset, parte.InputStream, parte.ContentLength, ObterPastaTemporariaChars());
                return Json(new { sucesso = true, recebido }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult AnalisarPacotesChars(string nome, string tokenEnvio = null)
        {
            try
            {
                CharacterArchiveBatchAnalysis analise;
                if (!string.IsNullOrWhiteSpace(tokenEnvio))
                    analise = CharacterArchiveImportService.AnalisarEnvio(tokenEnvio, ObterPastaTemporariaChars(), nome);
                else
                {
                    List<CharacterArchiveUpload> uploads = new List<CharacterArchiveUpload>();
                    for (int i = 0; i < Request.Files.Count; i++)
                    {
                        HttpPostedFileBase arquivo = Request.Files[i];
                        if (arquivo == null || arquivo.ContentLength <= 0 || string.IsNullOrWhiteSpace(arquivo.FileName))
                            continue;

                        uploads.Add(new CharacterArchiveUpload
                        {
                            FileName = arquivo.FileName,
                            Length = arquivo.ContentLength,
                            InputStream = arquivo.InputStream
                        });
                    }
                    analise = CharacterArchiveImportService.Analisar(uploads, ObterPastaTemporariaChars(), nome);
                }
                JsonResult response = Json(new
                {
                    sucesso = true,
                    token = analise.Token,
                    nome = analise.Nome,
                    candidatos = analise.Candidatos
                }, JsonRequestBehavior.DenyGet);
                response.MaxJsonLength = int.MaxValue;
                return response;
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult ExecutarTorneioChars(string token)
        {
            try
            {
                Server.ScriptTimeout = int.MaxValue;
                string stagesPath = MugenPaths.GetStagesPath();
                string stage = CenarioCatalogService.ObterStagesAtivos(_db, stagesPath).FirstOrDefault();
                IkemenPilotConfiguration ikemenConfiguration = IkemenPilotConfiguration.Load();
                CharacterTournamentResult resultado = CharacterArchiveImportService.ExecutarTorneio(
                    token,
                    ObterPastaTemporariaChars(),
                    MugenPaths.GetCharsPath(),
                    ikemenConfiguration,
                    stage,
                    Server.MapPath("~/App_Data/IkemenTournament"));

                JsonResult response = Json(new
                {
                    sucesso = true,
                    resultado,
                    mensagem = resultado.EmpateNaLideranca
                        ? "Torneio IKEMEN concluido. Houve empate tecnico; confira o ranking e escolha a versao desejada."
                        : "Torneio IKEMEN concluido. A versao com mais vitorias foi marcada como recomendada."
                }, JsonRequestBehavior.DenyGet);
                response.MaxJsonLength = int.MaxValue;
                return response;
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult ImportarPacoteChar(
            string token,
            string candidatoId,
            string nome,
            bool sobrescrever = false)
        {
            try
            {
                CharacterImportedPackage importado = CharacterArchiveImportService.Importar(
                    token,
                    candidatoId,
                    nome,
                    ObterPastaTemporariaChars(),
                    MugenPaths.GetCharsPath(),
                    sobrescrever,
                    true);

                return Json(new
                {
                    sucesso = true,
                    personagem = importado,
                    mensagem = importado.Nome + " foi instalado em chars com a pasta, o .def, name e displayname normalizados."
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult CancelarTesteChars(string token)
        {
            try
            {
                CharacterArchiveImportService.Cancelar(token, ObterPastaTemporariaChars());
                return Json(new { sucesso = true }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult AtualizarUniverso(int id, string universo)
        {
            var lutador = _db.Lutador.FirstOrDefault(x => x.Id == id);
            if (lutador == null)
                return Json(new { sucesso = false, mensagem = "Lutador nao encontrado." }, JsonRequestBehavior.DenyGet);

            string universoNormalizado = NormalizarNomeUniverso(universo);
            if (string.IsNullOrWhiteSpace(universoNormalizado))
                return Json(new { sucesso = false, mensagem = "Informe o universo." }, JsonRequestBehavior.DenyGet);

            if (universoNormalizado.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return Json(new { sucesso = false, mensagem = "Nome de universo invalido." }, JsonRequestBehavior.DenyGet);

            string imagemUniverso = Server.MapPath("~/Imagens/" + universoNormalizado + ".png");
            if (!System.IO.File.Exists(imagemUniverso))
                return Json(new { sucesso = false, mensagem = "Imagem do universo nao encontrada em /Imagens/" + universoNormalizado + ".png" }, JsonRequestBehavior.DenyGet);

            lutador.Universo = universoNormalizado;
            _db.SaveChanges();

            return Json(new { sucesso = true, universoNome = universoNormalizado, universo = "/Imagens/" + universoNormalizado + ".png" }, JsonRequestBehavior.DenyGet);
        }

        [HttpPost]
        public JsonResult AtualizarDetalhesLutador(
            int id,
            string nome,
            string autor,
            string universo,
            string tagIds,
            string life,
            string attack,
            string defence,
            string xscale,
            string yscale,
            HttpPostedFileBase portraitArquivo,
            HttpPostedFileBase standArquivo)
        {
            var lutador = _db.Lutador.FirstOrDefault(x => x.Id == id);
            if (lutador == null)
                return Json(new { sucesso = false, mensagem = "Lutador nao encontrado." }, JsonRequestBehavior.DenyGet);

            string nomeNormalizado = (nome ?? string.Empty).Trim();
            string autorNormalizado = (autor ?? string.Empty).Trim();
            string universoNormalizado = NormalizarNomeUniverso(universo);

            if (string.IsNullOrWhiteSpace(nomeNormalizado))
                return Json(new { sucesso = false, mensagem = "Informe o nome do lutador." }, JsonRequestBehavior.DenyGet);

            if (nomeNormalizado.Length > 255 || autorNormalizado.Length > 255)
                return Json(new { sucesso = false, mensagem = "Nome e autor devem possuir no maximo 255 caracteres." }, JsonRequestBehavior.DenyGet);

            bool nomeDuplicado = _db.Lutador.Any(x => x.Id != id && x.Nome == nomeNormalizado);
            if (nomeDuplicado)
                return Json(new { sucesso = false, mensagem = "Ja existe outro lutador com esse nome." }, JsonRequestBehavior.DenyGet);

            if (string.IsNullOrWhiteSpace(universoNormalizado))
                return Json(new { sucesso = false, mensagem = "Informe o universo do lutador." }, JsonRequestBehavior.DenyGet);

            if (universoNormalizado.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return Json(new { sucesso = false, mensagem = "Nome de universo invalido." }, JsonRequestBehavior.DenyGet);

            string imagemUniverso = Server.MapPath("~/Imagens/" + universoNormalizado + ".png");
            if (!System.IO.File.Exists(imagemUniverso))
                return Json(new { sucesso = false, mensagem = "Imagem do universo nao encontrada em /Imagens/" + universoNormalizado + ".png" }, JsonRequestBehavior.DenyGet);

            List<int> idsTags;
            try
            {
                idsTags = string.IsNullOrWhiteSpace(tagIds)
                    ? new List<int>()
                    : JsonSerializer.Deserialize<List<int>>(tagIds) ?? new List<int>();
                idsTags = idsTags.Where(x => x > 0).Distinct().ToList();
            }
            catch (JsonException)
            {
                return Json(new { sucesso = false, mensagem = "A selecao de tags e invalida." }, JsonRequestBehavior.DenyGet);
            }

            var tagsSelecionadas = _db.Tag.Where(x => idsTags.Contains(x.Id)).ToList();
            if (tagsSelecionadas.Count != idsTags.Count)
                return Json(new { sucesso = false, mensagem = "Uma ou mais tags selecionadas nao existem." }, JsonRequestBehavior.DenyGet);

            var atributosCns = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            string erroAtributo;
            if (!TryAddEditableCnsAttribute(atributosCns, "life", "Life", life, out erroAtributo)
                || !TryAddEditableCnsAttribute(atributosCns, "attack", "Attack", attack, out erroAtributo)
                || !TryAddEditableCnsAttribute(atributosCns, "defence", "Defense", defence, out erroAtributo)
                || !TryAddEditableCnsAttribute(atributosCns, "xscale", "X Scale", xscale, out erroAtributo)
                || !TryAddEditableCnsAttribute(atributosCns, "yscale", "Y Scale", yscale, out erroAtributo))
            {
                return Json(new { sucesso = false, mensagem = erroAtributo }, JsonRequestBehavior.DenyGet);
            }

            try
            {
                string extensaoPortrait = ValidarImagemLutador(portraitArquivo, "portrait");
                string extensaoStand = ValidarImagemLutador(standArquivo, "stand");
                bool nomeAlterado = !string.Equals(lutador.Nome, nomeNormalizado, StringComparison.Ordinal);
                PersonagemCharCadastro personagem = null;

                if (nomeAlterado || atributosCns.Count > 0)
                {
                    string charsPath = ObterCaminhoCharsConfigurado();
                    personagem = LocalizarPersonagemChar(charsPath, lutador);
                    if (atributosCns.Count > 0 && personagem == null)
                    {
                        return Json(new
                        {
                            sucesso = false,
                            mensagem = "Nao foi encontrada a pasta do personagem para atualizar os atributos do CNS."
                        }, JsonRequestBehavior.DenyGet);
                    }

                    if (personagem != null)
                        lutador.PastaPersonagem = personagem.Pasta;

                    if (atributosCns.Count > 0)
                        MugenCharacterSecurityService.UpdateEditableAttributes(personagem.DefPath, atributosCns);
                }

                if (!string.IsNullOrWhiteSpace(extensaoPortrait))
                    lutador.Portrait = SalvarImagemLutador(portraitArquivo, lutador.Id, "Portrait", extensaoPortrait);

                if (!string.IsNullOrWhiteSpace(extensaoStand))
                    lutador.Stand = SalvarImagemLutador(standArquivo, lutador.Id, "Stand", extensaoStand);

                lutador.Nome = nomeNormalizado;
                lutador.Autor = autorNormalizado;
                lutador.Universo = universoNormalizado;
                lutador.Tag.Clear();
                foreach (var tag in tagsSelecionadas)
                    lutador.Tag.Add(tag);

                _db.SaveChanges();

                var lutadorAtualizado = LutadorDTO.ToDTO(lutador);
                return Json(new { sucesso = true, lutador = lutadorAtualizado }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        private static bool TryAddEditableCnsAttribute(
            IDictionary<string, double> attributes,
            string key,
            string displayName,
            string rawValue,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(rawValue))
                return true;

            double value;
            bool parsed = double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(rawValue, NumberStyles.Float, new CultureInfo("pt-BR"), out value);
            if (!parsed || double.IsNaN(value) || double.IsInfinity(value) || value <= 0d)
            {
                error = displayName + " deve possuir um valor numerico maior que zero.";
                return false;
            }

            attributes[key] = value;
            return true;
        }

        [HttpPost]
        public JsonResult GetImpactoExclusao(int id)
        {
            var lutador = _db.Lutador.FirstOrDefault(x => x.Id == id);
            if (lutador == null)
                return Json(new { sucesso = false, mensagem = "Lutador nao encontrado." }, JsonRequestBehavior.DenyGet);

            int partidasRelacionadas = _db.Partida.Count(x =>
                x.IdLutadorCasa == id
                || x.IdLutadorVisitante == id
                || x.IdVencedor == id
                || x.idPerdedor == id);

            return Json(new
            {
                sucesso = true,
                nome = lutador.Nome,
                partidasRelacionadas
            }, JsonRequestBehavior.DenyGet);
        }

        [HttpPost]
        public JsonResult ExcluirLutador(int id)
        {
            var lutador = _db.Lutador.FirstOrDefault(x => x.Id == id);
            if (lutador == null)
                return Json(new { sucesso = false, mensagem = "Lutador nao encontrado." }, JsonRequestBehavior.DenyGet);

            using (var transaction = _db.Database.BeginTransaction())
            {
                try
                {
                    var partidasRelacionadas = _db.Partida.Where(x =>
                        x.IdLutadorCasa == id
                        || x.IdLutadorVisitante == id
                        || x.IdVencedor == id
                        || x.idPerdedor == id)
                        .ToList();

                    int partidasExcluidas = partidasRelacionadas.Count;
                    if (partidasRelacionadas.Any())
                        _db.Partida.RemoveRange(partidasRelacionadas);

                    lutador.Tag.Clear();
                    _db.Lutador.Remove(lutador);
                    _db.SaveChanges();
                    transaction.Commit();

                    return Json(new { sucesso = true, partidasExcluidas }, JsonRequestBehavior.DenyGet);
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return Json(new { sucesso = false, mensagem = "Nao foi possivel excluir o lutador. " + ex.Message }, JsonRequestBehavior.DenyGet);
                }
            }
        }

        [HttpPost]
        public JsonResult GetDetalhesLutador(LutadorDTO lutador)
        {
            PreencherDetalhesLutador(lutador);

            return Json(new { sucesso = true, lutador }, JsonRequestBehavior.DenyGet);
        }

        [HttpPost]
        public JsonResult ImportarStand(int id, string pastaPersonagem = null, bool sobrescrever = true, bool usarPaletaAct = true, int padding = 0, int? indiceTransparente = null, bool preservarCanvasSprite = false)
        {
            var lutador = _db.Lutador.FirstOrDefault(x => x.Id == id);
            if (lutador == null)
                return Json(new { sucesso = false, mensagem = "Lutador nao encontrado." }, JsonRequestBehavior.DenyGet);

            try
            {
                string charsPath = ObterCaminhoCharsConfigurado();
                var extractor = CriarExtratorStands();
                string outputFileName = string.IsNullOrWhiteSpace(lutador.Stand) ? null : Path.GetFileName(lutador.Stand);
                string pastaOrigem = ResolverPastaPersonagem(charsPath, lutador, pastaPersonagem);
                var resultado = extractor.ExtractStandFromCharacter(lutador.Nome, pastaOrigem, outputFileName, sobrescrever, 80, usarPaletaAct, padding, indiceTransparente, preservarCanvasSprite);

                lutador.Stand = resultado.OutputFileName;
                AtualizarPastaPersonagem(lutador, charsPath, resultado.CharacterFolder);
                _db.SaveChanges();

                return Json(new { sucesso = true, resultado, stand = ImageUrlVersioner.FromImages(resultado.OutputFileName) }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult ImportarPortrait(int id, string pastaPersonagem = null, bool sobrescrever = true, bool usarPaletaAct = true, int grupo = 9000, int imagem = 0, int? indiceTransparente = null)
        {
            var lutador = _db.Lutador.FirstOrDefault(x => x.Id == id);
            if (lutador == null)
                return Json(new { sucesso = false, mensagem = "Lutador nao encontrado." }, JsonRequestBehavior.DenyGet);

            try
            {
                string charsPath = ObterCaminhoCharsConfigurado();
                var extractor = CriarExtratorStands();
                string outputFileName = string.IsNullOrWhiteSpace(lutador.Portrait) ? null : Path.GetFileName(lutador.Portrait);
                string pastaOrigem = ResolverPastaPersonagem(charsPath, lutador, pastaPersonagem);
                var resultado = extractor.ExtractPortraitFromCharacter(lutador.Nome, pastaOrigem, outputFileName, sobrescrever, usarPaletaAct, grupo, imagem, indiceTransparente);

                lutador.Portrait = resultado.OutputFileName;
                AtualizarPastaPersonagem(lutador, charsPath, resultado.CharacterFolder);
                _db.SaveChanges();

                return Json(new { sucesso = true, resultado, portrait = ImageUrlVersioner.FromImages(resultado.OutputFileName) }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult ImportarStandDaPasta(string pastaPersonagem, string nomeArquivoSaida = null, bool sobrescrever = true, bool usarPaletaAct = true, int padding = 0, int? indiceTransparente = null, bool preservarCanvasSprite = false)
        {
            if (string.IsNullOrWhiteSpace(pastaPersonagem))
                return Json(new { sucesso = false, mensagem = "Informe a pasta do personagem." }, JsonRequestBehavior.DenyGet);

            try
            {
                var extractor = CriarExtratorStands();
                string nomePersonagem = Path.GetFileName(pastaPersonagem.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var resultado = extractor.ExtractStandFromCharacter(nomePersonagem, pastaPersonagem, nomeArquivoSaida, sobrescrever, 80, usarPaletaAct, padding, indiceTransparente, preservarCanvasSprite);

                return Json(new { sucesso = true, resultado, stand = ImageUrlVersioner.FromImages(resultado.OutputFileName) }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }
        }

        [HttpPost]
        public JsonResult ImportarStands(bool sobrescrever = false, bool usarPaletaAct = true, int padding = 0, int? indiceTransparente = null, bool preservarCanvasSprite = false)
        {
            MugenStandExtractor extractor;
            try
            {
                extractor = CriarExtratorStands();
            }
            catch (Exception ex)
            {
                return Json(new { sucesso = false, mensagem = ex.Message }, JsonRequestBehavior.DenyGet);
            }

            var resultados = new List<MugenStandExtractionResult>();
            var erros = new List<object>();
            int atualizados = 0;
            string charsPath = ObterCaminhoCharsConfigurado();

            foreach (var lutador in _db.Lutador.OrderBy(x => x.Nome).ToList())
            {
                try
                {
                    string outputFileName = string.IsNullOrWhiteSpace(lutador.Stand) ? null : Path.GetFileName(lutador.Stand);
                    string pastaOrigem = ResolverPastaPersonagem(charsPath, lutador, null);
                    var resultado = extractor.ExtractStandFromCharacter(lutador.Nome, pastaOrigem, outputFileName, sobrescrever, 80, usarPaletaAct, padding, indiceTransparente, preservarCanvasSprite);
                    resultados.Add(resultado);

                    lutador.Stand = resultado.OutputFileName;
                    AtualizarPastaPersonagem(lutador, charsPath, resultado.CharacterFolder);
                    atualizados++;
                }
                catch (Exception ex)
                {
                    erros.Add(new { lutador.Id, lutador.Nome, mensagem = ex.Message });
                }
            }

            if (atualizados > 0)
                _db.SaveChanges();

            var response = Json(new { sucesso = erros.Count == 0, atualizados, resultados, erros }, JsonRequestBehavior.DenyGet);
            response.MaxJsonLength = int.MaxValue;
            return response;
        }

        private List<RivalDTO> GetRivais(int idLutador)
        {
            var grupos = _db.Partida
                .Where(partida => partida.IdLutadorCasa == idLutador || partida.IdLutadorVisitante == idLutador)
                .GroupBy(partida => partida.IdLutadorCasa == idLutador ? partida.IdLutadorVisitante : partida.IdLutadorCasa)
                .OrderByDescending(g => g.Count())
                .ToList();

            var rivais = new List<RivalDTO>();

            foreach (var grupo in grupos)
            {
                var rivalDb = _db.Lutador.FirstOrDefault(x => x.Id == grupo.Key);
                if (rivalDb == null)
                    continue;

                var partidasContraRival = grupo
                    .OrderByDescending(partida => partida.DataPartida ?? DateTime.MinValue)
                    .ToList();

                rivais.Add(new RivalDTO
                {
                    Id = rivalDb.Id,
                    Nome = rivalDb.Nome,
                    Nivel = rivalDb.Nivel,
                    RatingElo = rivalDb.RatingElo,
                    Portrait = ImageUrlVersioner.FromImages(rivalDb.Portrait),
                    Vitorias = partidasContraRival.Count(partida => partida.IdVencedor == idLutador),
                    Derrotas = partidasContraRival.Count(partida => partida.idPerdedor == idLutador),
                    PartidasTotais = partidasContraRival.Count,
                    Partidas = partidasContraRival.Take(5).Select(partida => new PartidaDTO
                    {
                        Id = partida.Id,
                        IdVencedor = partida.IdVencedor,
                        IdPerdedor = partida.idPerdedor,
                    }).ToList()
                });
            }

            return rivais;
        }

        private void PreencherDetalhesLutador(LutadorDTO lutador)
        {
            var partidas = _db.Partida
                .Where(x => x.IdLutadorCasa == lutador.Id || x.IdLutadorVisitante == lutador.Id)
                .ToList();

            lutador.Vitorias = partidas.Count(x => x.IdVencedor == lutador.Id);
            lutador.Derrotas = partidas.Count(x => x.idPerdedor == lutador.Id);
            lutador.Partidas = lutador.Vitorias + lutador.Derrotas;
            lutador.Rivais = GetRivais(lutador.Id);
            lutador.UltimasPartidas = partidas
                .OrderByDescending(x => x.DataPartida ?? DateTime.MinValue)
                .Take(8)
                .Select(PartidaDTO.ToDTO)
                .ToList();
            lutador.HistoricoRatingElo = ObterHistoricoRatingElo(lutador.Id, lutador.RatingElo);
            DivisionService.PreencherLutador(_db, lutador);
        }

        private List<EloHistoricoDTO> ObterHistoricoRatingElo(int lutadorId, int ratingEloAtual)
        {
            var partidas = _db.Partida
                .AsNoTracking()
                .Where(x => x.IdVencedor.HasValue
                    && x.idPerdedor.HasValue
                    && x.IdVencedor.Value != x.idPerdedor.Value)
                .Select(x => new EloHistoryMatch
                {
                    Id = x.Id,
                    DataPartida = x.DataPartida,
                    IdVencedor = x.IdVencedor,
                    IdPerdedor = x.idPerdedor,
                    PontosVencedor = x.PontosVencedor,
                    PontosPerdedor = x.PontosPerdedor
                })
                .ToList();

            List<EloHistoryPoint> pontos = EloHistoryService.Calcular(partidas, lutadorId, ratingEloAtual);
            List<int> adversariosIds = pontos
                .Select(x => x.AdversarioId)
                .Distinct()
                .ToList();

            Dictionary<int, Lutador> adversarios = _db.Lutador
                .AsNoTracking()
                .Where(x => adversariosIds.Contains(x.Id))
                .ToList()
                .ToDictionary(x => x.Id);

            return pontos.Select(ponto =>
            {
                Lutador adversario;
                adversarios.TryGetValue(ponto.AdversarioId, out adversario);

                return new EloHistoricoDTO
                {
                    PartidaId = ponto.PartidaId,
                    DataPartida = ponto.DataPartida.HasValue
                        ? ponto.DataPartida.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"))
                        : "Data desconhecida",
                    AdversarioId = ponto.AdversarioId,
                    AdversarioNome = adversario != null ? adversario.Nome : "Lutador removido",
                    AdversarioPortrait = ImageUrlVersioner.FromImages(adversario != null ? adversario.Portrait : null),
                    Vitoria = ponto.Vitoria,
                    RatingEloAnterior = ponto.RatingEloAnterior,
                    RatingEloAtual = ponto.RatingEloAtual,
                    RatingEloAdversario = ponto.RatingEloAdversario,
                    VariacaoRatingElo = ponto.VariacaoRatingElo,
                    PontosLutador = ponto.PontosLutador,
                    PontosAdversario = ponto.PontosAdversario
                };
            }).ToList();
        }

        private MugenStandExtractor CriarExtratorStands()
        {
            string charsPath = ObterCaminhoCharsConfigurado();
            string outputPath = Server.MapPath("~/Imagens");

            return new MugenStandExtractor(charsPath, outputPath);
        }

        private List<PersonagemCharCadastro> ObterPersonagensCharsDisponiveis()
        {
            string charsPath = ObterCaminhoCharsConfigurado();

            var nomesExistentes = new HashSet<string>(
                _db.Lutador.Select(x => x.Nome).ToList().Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            var pastasExistentes = new HashSet<string>(
                _db.Lutador.Select(x => x.PastaPersonagem).ToList().Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);

            var nomesEncontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var personagens = new List<PersonagemCharCadastro>();

            foreach (var defPath in ObterDefsPrincipaisDosChars(charsPath))
            {
                var personagem = LerPersonagemChar(defPath, charsPath);
                if (personagem == null || string.IsNullOrWhiteSpace(personagem.Nome))
                    continue;

                if (nomesExistentes.Contains(personagem.Nome)
                    || nomesEncontrados.Contains(personagem.Nome)
                    || pastasExistentes.Contains(personagem.Pasta))
                    continue;

                nomesEncontrados.Add(personagem.Nome);
                personagens.Add(personagem);
            }

            return personagens.OrderBy(x => x.Nome).ToList();
        }

        private static string ObterCaminhoCharsConfigurado()
        {
            return MugenPaths.GetCharsPath();
        }

        private string ObterPastaTemporariaChars()
        {
            return Server.MapPath("~/App_Data/CharImports");
        }

        private static PersonagemCharCadastro LocalizarPersonagemChar(string charsPath, string nomeLutador)
        {
            return LocalizarPersonagemChar(ObterDefsPrincipaisDosChars(charsPath)
                .Select(defPath => LerPersonagemChar(defPath, charsPath)), nomeLutador);
        }

        private static PersonagemCharCadastro LocalizarPersonagemChar(string charsPath, Lutador lutador)
        {
            return LocalizarPersonagemChar(
                ObterDefsPrincipaisDosChars(charsPath).Select(defPath => LerPersonagemChar(defPath, charsPath)),
                lutador);
        }

        private static PersonagemCharCadastro LocalizarPersonagemChar(
            IEnumerable<PersonagemCharCadastro> personagens,
            Lutador lutador)
        {
            if (lutador == null)
                return null;

            List<PersonagemCharCadastro> lista = personagens.Where(x => x != null).ToList();
            if (!string.IsNullOrWhiteSpace(lutador.PastaPersonagem))
            {
                string pastaPersistida = NormalizarPastaPersonagem(lutador.PastaPersonagem);
                PersonagemCharCadastro porPasta = lista.FirstOrDefault(x =>
                    string.Equals(NormalizarPastaPersonagem(x.Pasta), pastaPersistida, StringComparison.OrdinalIgnoreCase));
                if (porPasta != null)
                    return porPasta;
            }

            var identificadores = new[]
            {
                lutador.Nome,
                ExtrairNomePersonagemDaImagem(lutador.Stand, "Stand"),
                ExtrairNomePersonagemDaImagem(lutador.Portrait, "Portrait")
            };

            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string identificador in identificadores)
            {
                string normalizado = NormalizarIdentificadorPersonagem(identificador);
                if (string.IsNullOrWhiteSpace(normalizado) || !usados.Add(normalizado))
                    continue;

                PersonagemCharCadastro personagem = LocalizarPersonagemChar(lista, identificador);
                if (personagem != null)
                    return personagem;
            }

            return null;
        }

        private static string ResolverPastaPersonagem(string charsPath, Lutador lutador, string pastaInformada)
        {
            if (!string.IsNullOrWhiteSpace(pastaInformada))
                return pastaInformada;

            PersonagemCharCadastro personagem = LocalizarPersonagemChar(charsPath, lutador);
            return personagem == null ? null : personagem.Pasta;
        }

        private static void AtualizarPastaPersonagem(Lutador lutador, string charsPath, string pastaResolvida)
        {
            if (lutador == null || string.IsNullOrWhiteSpace(charsPath) || string.IsNullOrWhiteSpace(pastaResolvida))
                return;

            string root = Path.GetFullPath(charsPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string folder = Path.GetFullPath(pastaResolvida);
            if (folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                lutador.PastaPersonagem = ObterPastaRelativa(charsPath, folder);
        }

        private static string ExtrairNomePersonagemDaImagem(string arquivo, string sufixo)
        {
            if (string.IsNullOrWhiteSpace(arquivo))
                return null;

            string nome = Path.GetFileNameWithoutExtension(arquivo);
            if (string.IsNullOrWhiteSpace(nome) || !nome.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase))
                return null;

            nome = nome.Substring(0, nome.Length - sufixo.Length).Trim();
            return string.IsNullOrWhiteSpace(nome) ? null : nome;
        }

        private static string NormalizarPastaPersonagem(string pasta)
        {
            return (pasta ?? string.Empty)
                .Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        private static PersonagemCharCadastro LocalizarPersonagemChar(IEnumerable<PersonagemCharCadastro> personagens, string nomeLutador)
        {
            string nomeNormalizado = NormalizarIdentificadorPersonagem(nomeLutador);
            if (string.IsNullOrWhiteSpace(nomeNormalizado))
                return null;

            return personagens.Where(x => x != null)
                .Select(personagem =>
                {
                    string nomePasta = Path.GetFileName((personagem.Pasta ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    string nomeDef = Path.GetFileNameWithoutExtension(personagem.DefPath);
                    int pontuacao = 0;

                    if (string.Equals((personagem.Nome ?? string.Empty).Trim(), (nomeLutador ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                        pontuacao += 8;
                    else if (NormalizarIdentificadorPersonagem(personagem.Nome) == nomeNormalizado)
                        pontuacao += 4;

                    if (NormalizarIdentificadorPersonagem(nomePasta) == nomeNormalizado)
                        pontuacao += 3;

                    if (NormalizarIdentificadorPersonagem(nomeDef) == nomeNormalizado)
                        pontuacao += 2;

                    return new { personagem, pontuacao };
                })
                .Where(x => x.personagem != null && x.pontuacao > 0)
                .OrderByDescending(x => x.pontuacao)
                .ThenBy(x => x.personagem.Pasta)
                .Select(x => x.personagem)
                .FirstOrDefault();
        }

        private static string NormalizarIdentificadorPersonagem(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalizado = value
                .Trim()
                .Normalize(NormalizationForm.FormD)
                .ToLowerInvariant();

            return new string(normalizado
                .Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark)
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .Normalize(NormalizationForm.FormC);
        }

        private static List<string> ObterDefsPrincipaisDosChars(string charsPath)
        {
            var defPaths = new List<string>();

            foreach (string characterFolder in Directory.GetDirectories(charsPath)
                .Where(x => !EhPastaTemporariaDreamMover(Path.GetFileName(x))))
            {
                string folderName = Path.GetFileName(characterFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string preferredDef = Path.Combine(characterFolder, folderName + ".def");
                string defPath = System.IO.File.Exists(preferredDef)
                    ? preferredDef
                    : Directory.GetFiles(characterFolder, "*.def", SearchOption.TopDirectoryOnly).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(defPath))
                    defPaths.Add(defPath);
            }

            defPaths.AddRange(Directory.GetFiles(charsPath, "*.def", SearchOption.TopDirectoryOnly));
            return defPaths;
        }

        private static bool EhPastaTemporariaDreamMover(string nomePasta)
        {
            return !string.IsNullOrWhiteSpace(nomePasta)
                && (nomePasta.StartsWith("__dmtest_", StringComparison.OrdinalIgnoreCase)
                    || nomePasta.StartsWith("__dmimport_", StringComparison.OrdinalIgnoreCase)
                    || nomePasta.StartsWith("__dmbackup_", StringComparison.OrdinalIgnoreCase));
        }

        private static PersonagemCharCadastro LerPersonagemChar(string defPath, string charsPath)
        {
            string nome = string.Empty;
            string nomeInterno = string.Empty;
            string autor = string.Empty;

            foreach (string rawLine in System.IO.File.ReadAllLines(defPath))
            {
                string line = RemoverComentarioDef(rawLine).Trim();
                if (line.Length == 0)
                    continue;

                int equalIndex = line.IndexOf('=');
                if (equalIndex < 0)
                    continue;

                string key = line.Substring(0, equalIndex).Trim().ToLowerInvariant();
                string value = line.Substring(equalIndex + 1);

                if (key == "displayname")
                    nome = LimparValorDef(value, false);
                else if (key == "name")
                    nomeInterno = LimparValorDef(value, false);
                else if (key == "author")
                    autor = LimparValorDef(value, false);
            }

            if (string.IsNullOrWhiteSpace(nome))
                nome = !string.IsNullOrWhiteSpace(nomeInterno)
                    ? nomeInterno
                    : RetornaNomeSemCaracteresInvalidos(Path.GetFileNameWithoutExtension(defPath).ToLowerInvariant());

            string characterFolder = Path.GetDirectoryName(defPath);
            return new PersonagemCharCadastro
            {
                Nome = nome,
                Autor = autor,
                Pasta = ObterPastaRelativa(charsPath, characterFolder),
                DefPath = defPath
            };
        }

        private static string RemoverComentarioDef(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            int commentIndex = value.IndexOf(';');
            return commentIndex >= 0 ? value.Substring(0, commentIndex) : value;
        }

        private static string LimparValorDef(string value, bool lowerCase)
        {
            string clean = RemoverComentarioDef(value).Trim().Trim('"').Trim('\'').Trim();

            if (lowerCase)
                clean = clean.ToLowerInvariant();

            return RetornaNomeSemCaracteresInvalidos(clean);
        }

        private static string RetornaNomeSemCaracteresInvalidos(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            char[] invalidChars = Path.GetInvalidFileNameChars();
            string clean = new string(value.Where(x => !invalidChars.Contains(x) && x != '"').ToArray()).Trim();
            return string.Join(" ", clean.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string ObterPastaRelativa(string rootPath, string folderPath)
        {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string folder = Path.GetFullPath(folderPath);

            if (folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return folder.Substring(root.Length);

            return Path.GetFileName(folder);
        }

        private static bool EhImagemDeUniverso(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome))
                return false;

            return nome.IndexOf("Portrait", StringComparison.OrdinalIgnoreCase) < 0
                && nome.IndexOf("Stand", StringComparison.OrdinalIgnoreCase) < 0
                && !nome.StartsWith("Dashboard", StringComparison.OrdinalIgnoreCase)
                && !nome.Equals("Versus", StringComparison.OrdinalIgnoreCase)
                && !nome.Equals("StarIcon", StringComparison.OrdinalIgnoreCase)
                && !nome.Equals("win", StringComparison.OrdinalIgnoreCase)
                && !nome.Equals("loss", StringComparison.OrdinalIgnoreCase)
                && !nome.Equals("shadowPortrait", StringComparison.OrdinalIgnoreCase)
                && !nome.Equals("shadowStand", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizarNomeUniverso(string universo)
        {
            if (string.IsNullOrWhiteSpace(universo))
                return string.Empty;

            string nome = universo.Trim().Replace("\\", "/");
            int barra = nome.LastIndexOf('/');
            if (barra >= 0)
                nome = nome.Substring(barra + 1);

            if (nome.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                nome = Path.GetFileNameWithoutExtension(nome);

            return nome.Trim();
        }

        private string ValidarImagemLutador(HttpPostedFileBase arquivo, string tipo)
        {
            if (arquivo == null || arquivo.ContentLength <= 0)
                return null;

            const int tamanhoMaximo = 8 * 1024 * 1024;
            if (arquivo.ContentLength > tamanhoMaximo)
                throw new InvalidOperationException("O arquivo de " + tipo + " deve possuir no maximo 8 MB.");

            string extensao = Path.GetExtension(Path.GetFileName(arquivo.FileName));
            var extensoesPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".gif", ".webp"
            };

            if (string.IsNullOrWhiteSpace(extensao) || !extensoesPermitidas.Contains(extensao))
                throw new InvalidOperationException("Formato de " + tipo + " invalido. Utilize PNG, JPG, GIF ou WEBP.");

            return extensao.ToLowerInvariant();
        }

        private string SalvarImagemLutador(HttpPostedFileBase arquivo, int idLutador, string tipo, string extensao)
        {
            string nomeArquivo = idLutador + "_" + tipo + extensao;
            string caminhoImagens = Server.MapPath("~/Imagens");

            if (!Directory.Exists(caminhoImagens))
                Directory.CreateDirectory(caminhoImagens);

            arquivo.SaveAs(Path.Combine(caminhoImagens, nomeArquivo));
            return nomeArquivo;
        }

        private class PersonagemCharCadastro
        {
            public string Nome { get; set; }
            public string Autor { get; set; }
            public string Pasta { get; set; }
            public string DefPath { get; set; }
        }

    }
}

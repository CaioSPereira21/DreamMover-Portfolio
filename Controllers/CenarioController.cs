using DreamMover.DTO;
using DreamMover.Helpers;
using DreamMover.Models;
using DreamMover.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace DreamMover.Controllers
{
    public class CenarioController : Controller
    {
        private static readonly HashSet<string> ExtensoesPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".def", ".sff", ".snd", ".glb", ".hdr", ".chr", ".zss", ".mp3", ".ogg", ".mid", ".wav", ".png", ".jpg", ".jpeg"
        };

        private readonly lutasEntities _db = new lutasEntities();

        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public JsonResult Listar()
        {
            try
            {
                string stagesPath = MugenPaths.GetStagesPath();
                int importados = CenarioCatalogService.SincronizarPasta(_db, stagesPath);
                List<CenarioDTO> cenarios = _db.Cenario
                    .AsNoTracking()
                    .OrderBy(x => x.Nome)
                    .ToList()
                    .Select(x => CenarioDTO.ToDTO(x, stagesPath))
                    .ToList();

                JsonResult response = Json(new { sucesso = true, cenarios, importados }, JsonRequestBehavior.DenyGet);
                response.MaxJsonLength = int.MaxValue;
                return response;
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult Sincronizar()
        {
            try
            {
                int importados = CenarioCatalogService.SincronizarPasta(_db, MugenPaths.GetStagesPath());
                return Json(new
                {
                    sucesso = true,
                    importados,
                    mensagem = importados == 0
                        ? "A pasta stages ja esta sincronizada."
                        : importados + " cenario(s) adicionado(s) ao catalogo."
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult AnalisarPacotes()
        {
            try
            {
                List<StageArchiveUpload> uploads = ObterArquivosEnviados()
                    .Select(x => new StageArchiveUpload
                    {
                        FileName = x.FileName,
                        Length = x.ContentLength,
                        InputStream = x.InputStream
                    })
                    .ToList();

                StageArchiveBatchAnalysis analise = StageArchiveImportService.Analisar(
                    uploads,
                    ObterPastaTemporariaImportacao());
                JsonResult response = Json(new
                {
                    sucesso = true,
                    token = analise.Token,
                    pacotes = analise.Pacotes
                }, JsonRequestBehavior.DenyGet);
                response.MaxJsonLength = int.MaxValue;
                return response;
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult ImportarPacotes(
            string token,
            string selecoes,
            string nome,
            string universo,
            string autor,
            bool sobrescreverArquivos = false)
        {
            try
            {
                List<StagePackageSelection> pacotes = JsonConvert.DeserializeObject<List<StagePackageSelection>>(selecoes ?? string.Empty)
                    ?? new List<StagePackageSelection>();
                List<StageAdditionalMusic> musicasAdicionais = new List<StageAdditionalMusic>();
                foreach (StagePackageSelection pacote in pacotes.Where(x => x != null && x.Importar))
                {
                    HttpPostedFileBase musica = Request.Files["musica_" + pacote.PacoteId];
                    if (musica == null || musica.ContentLength <= 0)
                        continue;

                    musicasAdicionais.Add(new StageAdditionalMusic
                    {
                        PacoteId = pacote.PacoteId,
                        FileName = musica.FileName,
                        Length = musica.ContentLength,
                        InputStream = musica.InputStream
                    });
                }

                string stagesPath = MugenPaths.GetStagesPath();
                List<StageImportedPackage> importados = StageArchiveImportService.Importar(
                    token,
                    pacotes,
                    musicasAdicionais,
                    ObterPastaTemporariaImportacao(),
                    stagesPath,
                    sobrescreverArquivos);

                CenarioCatalogService.SincronizarPasta(_db, stagesPath);
                bool cadastroUnico = importados.Count == 1;
                string universoNormalizado = NormalizarTexto(universo, 255);
                List<CenarioDTO> cenarios = new List<CenarioDTO>();
                foreach (StageImportedPackage importado in importados)
                {
                    Cenario cenario = _db.Cenario.FirstOrDefault(x => x.ArquivoDef == importado.ArquivoDef);
                    if (cenario == null)
                        throw new InvalidOperationException("O pacote foi copiado, mas o cenario " + importado.ArquivoDef + " nao foi localizado no catalogo.");

                    CenarioDefInfo info = CenarioCatalogService.LerInformacoesDef(Path.Combine(stagesPath, importado.ArquivoDef));
                    cenario.Nome = cadastroUnico
                        ? NormalizarTexto(nome, 255) ?? info.Nome ?? Path.GetFileNameWithoutExtension(importado.ArquivoDef)
                        : info.Nome ?? Path.GetFileNameWithoutExtension(importado.ArquivoDef);
                    cenario.Universo = universoNormalizado;
                    cenario.Autor = cadastroUnico ? NormalizarTexto(autor, 255) ?? info.Autor : info.Autor;
                    cenario.Ativo = true;
                    cenario.Excluido = false;
                    cenario.DataAtualizacao = DateTime.Now;
                    cenarios.Add(CenarioDTO.ToDTO(cenario, stagesPath));
                }

                _db.SaveChanges();
                cenarios = importados.Select(x =>
                {
                    Cenario cenario = _db.Cenario.First(y => y.ArquivoDef == x.ArquivoDef);
                    return CenarioDTO.ToDTO(cenario, stagesPath);
                }).ToList();
                StageArchiveImportService.Cancelar(token, ObterPastaTemporariaImportacao());

                return Json(new
                {
                    sucesso = true,
                    cenarios,
                    quantidade = cenarios.Count,
                    mensagem = cenarios.Count == 1
                        ? "Cenario importado; a musica e a referencia bgmusic foram corrigidas."
                        : cenarios.Count + " cenarios importados; as musicas e referencias bgmusic foram corrigidas."
                }, JsonRequestBehavior.DenyGet);
            }
            catch (StageMusicRequiredException ex)
            {
                return Json(new
                {
                    sucesso = false,
                    musicaObrigatoria = true,
                    pacoteId = ex.PacoteId,
                    mensagem = ex.Message
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult CancelarImportacao(string token)
        {
            try
            {
                StageArchiveImportService.Cancelar(token, ObterPastaTemporariaImportacao());
                return Json(new { sucesso = true }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult Cadastrar(string nome, string universo, string autor, bool sobrescreverArquivos = false)
        {
            try
            {
                List<HttpPostedFileBase> arquivos = ObterArquivosEnviados();
                HttpPostedFileBase arquivoDef = arquivos.FirstOrDefault(x => Path.GetExtension(Path.GetFileName(x.FileName)).Equals(".def", StringComparison.OrdinalIgnoreCase));
                List<HttpPostedFileBase> arquivosMusica = arquivos
                    .Where(x => MugenStageMusic.EhArquivoDeMusica(x.FileName))
                    .ToList();

                if (arquivoDef == null)
                    return Json(new { sucesso = false, mensagem = "Selecione exatamente um arquivo .def do cenario." }, JsonRequestBehavior.DenyGet);

                if (arquivos.Count(x => Path.GetExtension(Path.GetFileName(x.FileName)).Equals(".def", StringComparison.OrdinalIgnoreCase)) != 1)
                    return Json(new { sucesso = false, mensagem = "Envie somente um arquivo .def por cadastro." }, JsonRequestBehavior.DenyGet);

                if (arquivosMusica.Count > 1)
                    return Json(new { sucesso = false, mensagem = "Envie somente uma musica por cenario." }, JsonRequestBehavior.DenyGet);

                if (arquivosMusica.Count == 0)
                    return Json(new
                    {
                        sucesso = false,
                        musicaObrigatoria = true,
                        mensagem = "Este cenario nao possui musica. Adicione uma musica ou cancele a adicao."
                    }, JsonRequestBehavior.DenyGet);

                string stagesPath = MugenPaths.GetStagesPath();
                Dictionary<HttpPostedFileBase, string> nomesDestino = arquivos.ToDictionary(
                    x => x,
                    x => Path.GetFileName(x.FileName));

                HttpPostedFileBase arquivoMusica = arquivosMusica.SingleOrDefault();
                if (arquivoMusica != null)
                    nomesDestino[arquivoMusica] = MugenStageMusic.ObterNomeNormalizado(arquivoDef.FileName, arquivoMusica.FileName);

                ValidarArquivos(nomesDestino, stagesPath, sobrescreverArquivos);

                foreach (HttpPostedFileBase arquivo in arquivos)
                {
                    string fileName = nomesDestino[arquivo];
                    arquivo.SaveAs(Path.Combine(stagesPath, fileName));
                }

                string defFileName = nomesDestino[arquivoDef];
                if (arquivoMusica != null)
                    MugenStageMusic.CorrigirReferenciaNoDef(
                        Path.Combine(stagesPath, defFileName),
                        nomesDestino[arquivoMusica]);

                CenarioCatalogService.SincronizarPasta(_db, stagesPath);
                Cenario cenario = _db.Cenario.FirstOrDefault(x => x.ArquivoDef == defFileName);
                if (cenario == null)
                    throw new InvalidOperationException("O arquivo foi salvo, mas o cenario nao foi localizado no catalogo.");

                CenarioDefInfo info = CenarioCatalogService.LerInformacoesDef(Path.Combine(stagesPath, defFileName));
                cenario.Nome = NormalizarTexto(nome, 255) ?? info.Nome ?? Path.GetFileNameWithoutExtension(defFileName);
                cenario.Universo = NormalizarTexto(universo, 255);
                cenario.Autor = NormalizarTexto(autor, 255) ?? info.Autor;
                cenario.Ativo = true;
                cenario.Excluido = false;
                cenario.DataAtualizacao = DateTime.Now;
                _db.SaveChanges();

                return Json(new
                {
                    sucesso = true,
                    cenario = CenarioDTO.ToDTO(cenario, stagesPath),
                    mensagem = "Cenario cadastrado; a musica e a referencia bgmusic foram corrigidas."
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult Atualizar(int id, string nome, string universo, string autor, bool ativo, string cenas)
        {
            try
            {
                Cenario cenario = _db.Cenario.FirstOrDefault(x => x.Id == id);
                if (cenario == null)
                    return Json(new { sucesso = false, mensagem = "Cenario nao encontrado." }, JsonRequestBehavior.DenyGet);

                string nomeNormalizado = NormalizarTexto(nome, 255);
                if (string.IsNullOrWhiteSpace(nomeNormalizado))
                    return Json(new { sucesso = false, mensagem = "Informe o nome do cenario." }, JsonRequestBehavior.DenyGet);

                string stagesPath = MugenPaths.GetStagesPath();
                string stagePath = Path.Combine(stagesPath, cenario.ArquivoDef ?? string.Empty);
                if (!System.IO.File.Exists(stagePath))
                    return Json(new { sucesso = false, mensagem = "Arquivo .def do cenario nao encontrado." }, JsonRequestBehavior.DenyGet);

                List<string> nomesDisponiveis = _db.Cenario
                    .AsNoTracking()
                    .Where(x => !x.Excluido)
                    .ToList()
                    .Where(x => !string.IsNullOrWhiteSpace(x.Nome)
                        && !string.IsNullOrWhiteSpace(x.ArquivoDef)
                        && System.IO.File.Exists(Path.Combine(stagesPath, x.ArquivoDef)))
                    .Select(x => x.Id == cenario.Id ? nomeNormalizado : x.Nome.Trim())
                    .ToList();

                cenario.Nome = nomeNormalizado;
                cenario.Universo = NormalizarTexto(universo, 255);
                cenario.Autor = NormalizarTexto(autor, 255);
                cenario.Ativo = ativo;
                cenario.CenasJson = CenarioSceneConfiguration.ValidarESerializar(cenas, nomesDisponiveis);
                cenario.DataAtualizacao = DateTime.Now;
                _db.SaveChanges();

                return Json(new
                {
                    sucesso = true,
                    cenario = CenarioDTO.ToDTO(cenario, stagesPath),
                    mensagem = string.IsNullOrWhiteSpace(cenario.CenasJson)
                        ? "Cenario atualizado."
                        : "Cenario e roteiro de cenas atualizados."
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult ObterConfiguracaoCenas(int id)
        {
            try
            {
                Cenario cenario = _db.Cenario.AsNoTracking().FirstOrDefault(x => x.Id == id && !x.Excluido);
                if (cenario == null)
                    return Json(new { sucesso = false, mensagem = "Cenario nao encontrado." }, JsonRequestBehavior.DenyGet);

                string stagesPath = MugenPaths.GetStagesPath();
                string stagePath = Path.Combine(stagesPath, cenario.ArquivoDef ?? string.Empty);
                if (!System.IO.File.Exists(stagePath))
                    return Json(new { sucesso = false, mensagem = "Arquivo .def do cenario nao encontrado." }, JsonRequestBehavior.DenyGet);

                return Json(new
                {
                    sucesso = true,
                    cenas = CenarioSceneConfiguration.Deserializar(cenario.CenasJson),
                    cenarioAtual = cenario.Nome,
                    cenariosDisponiveis = _db.Cenario
                        .AsNoTracking()
                        .Where(x => !x.Excluido)
                        .OrderBy(x => x.Nome)
                        .ToList()
                        .Where(x => !string.IsNullOrWhiteSpace(x.Nome)
                            && !string.IsNullOrWhiteSpace(x.ArquivoDef)
                            && System.IO.File.Exists(Path.Combine(stagesPath, x.ArquivoDef)))
                        .Select(x => new
                        {
                            x.Id,
                            x.Nome,
                            x.Ativo,
                            x.ArquivoDef
                        })
                        .ToList()
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult Excluir(int id)
        {
            Cenario cenario = _db.Cenario.FirstOrDefault(x => x.Id == id);
            if (cenario == null)
                return Json(new { sucesso = false, mensagem = "Cenario nao encontrado." }, JsonRequestBehavior.DenyGet);

            cenario.Excluido = true;
            cenario.Ativo = false;
            cenario.DataAtualizacao = DateTime.Now;
            _db.SaveChanges();

            return Json(new
            {
                sucesso = true,
                mensagem = "Cenario excluido do DreamMover. Os arquivos em stages foram preservados."
            }, JsonRequestBehavior.DenyGet);
        }

        [HttpPost]
        public JsonResult Restaurar(int id)
        {
            Cenario cenario = _db.Cenario.FirstOrDefault(x => x.Id == id);
            if (cenario == null)
                return Json(new { sucesso = false, mensagem = "Cenario nao encontrado." }, JsonRequestBehavior.DenyGet);

            cenario.Excluido = false;
            cenario.Ativo = true;
            cenario.DataAtualizacao = DateTime.Now;
            _db.SaveChanges();

            return Json(new
            {
                sucesso = true,
                cenario = CenarioDTO.ToDTO(cenario, MugenPaths.GetStagesPath()),
                mensagem = "Cenario restaurado."
            }, JsonRequestBehavior.DenyGet);
        }

        [HttpPost]
        public JsonResult LerDef(int id)
        {
            try
            {
                Cenario cenario = _db.Cenario
                    .AsNoTracking()
                    .FirstOrDefault(x => x.Id == id);
                if (cenario == null)
                    return Json(new { sucesso = false, mensagem = "Cenario nao encontrado." }, JsonRequestBehavior.DenyGet);

                string arquivoDef = (cenario.ArquivoDef ?? string.Empty).Trim().Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(arquivoDef)
                    || !arquivoDef.EndsWith(".def", StringComparison.OrdinalIgnoreCase)
                    || arquivoDef.StartsWith("/", StringComparison.Ordinal)
                    || Path.IsPathRooted(arquivoDef.Replace('/', Path.DirectorySeparatorChar))
                    || arquivoDef.Contains(":")
                    || arquivoDef.Split('/').Any(x => x == "." || x == ".."))
                    return Json(new { sucesso = false, mensagem = "Referencia de arquivo .def invalida." }, JsonRequestBehavior.DenyGet);

                string stagesPath = Path.GetFullPath(MugenPaths.GetStagesPath());
                string raizStages = stagesPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string caminhoDef = Path.GetFullPath(Path.Combine(
                    stagesPath,
                    arquivoDef.Replace('/', Path.DirectorySeparatorChar)));
                if (!caminhoDef.StartsWith(raizStages, StringComparison.OrdinalIgnoreCase))
                    return Json(new { sucesso = false, mensagem = "O arquivo informado nao pertence a pasta stages." }, JsonRequestBehavior.DenyGet);
                if (!System.IO.File.Exists(caminhoDef))
                    return Json(new { sucesso = false, mensagem = "Arquivo .def do cenario nao encontrado." }, JsonRequestBehavior.DenyGet);
                if (new FileInfo(caminhoDef).Length > 5L * 1024L * 1024L)
                    return Json(new { sucesso = false, mensagem = "O arquivo .def e muito grande para visualizacao." }, JsonRequestBehavior.DenyGet);

                string conteudo;
                using (StreamReader reader = new StreamReader(caminhoDef, Encoding.Default, true))
                    conteudo = reader.ReadToEnd();

                JsonResult response = Json(new
                {
                    sucesso = true,
                    nome = cenario.Nome,
                    arquivoDef,
                    conteudo
                }, JsonRequestBehavior.DenyGet);
                response.MaxJsonLength = int.MaxValue;
                return response;
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult GerarImagem(int id)
        {
            Cenario cenario = _db.Cenario.FirstOrDefault(x => x.Id == id);
            if (cenario == null || cenario.Excluido)
                return Json(new { sucesso = false, mensagem = "Cenario nao encontrado." }, JsonRequestBehavior.DenyGet);

            try
            {
                IkemenPilotConfiguration ikemenConfiguration = IkemenPilotConfiguration.Load();
                if (!ikemenConfiguration.IsConfigured)
                    return Json(new { sucesso = false, mensagem = ikemenConfiguration.ConfigurationMessage }, JsonRequestBehavior.DenyGet);

                string stagePath = Path.Combine(
                    ikemenConfiguration.RootPath,
                    "stages",
                    cenario.ArquivoDef);
                if (!System.IO.File.Exists(stagePath))
                    return Json(new { sucesso = false, mensagem = "Arquivo .def nao encontrado na pasta stages do IKEMEN." }, JsonRequestBehavior.DenyGet);

                string outputFileName = "cenario-" + cenario.Id + "-" + SanitizeFileName(cenario.Nome) + ".png";
                string outputPath = Server.MapPath("~/Imagens/Cenarios/" + outputFileName);
                string player1 = ConfigurationManager.AppSettings["IkemenPreviewPlayer1"];
                string player2 = ConfigurationManager.AppSettings["IkemenPreviewPlayer2"];
                if (string.IsNullOrWhiteSpace(player1))
                    player1 = ikemenConfiguration.Player1;
                if (string.IsNullOrWhiteSpace(player2))
                    player2 = ikemenConfiguration.Player2;

                IkemenStagePreviewResult result = new IkemenStagePreviewGenerator(ikemenConfiguration).Generate(
                    cenario.ArquivoDef,
                    player1,
                    player2,
                    outputPath);

                cenario.Imagem = outputFileName;
                cenario.DataAtualizacao = DateTime.Now;
                _db.SaveChanges();

                return Json(new
                {
                    sucesso = true,
                    imagem = "/Imagens/Cenarios/" + outputFileName,
                    largura = result.Width,
                    altura = result.Height,
                    mensagem = "Imagem capturada no IKEMEN."
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpGet]
        public ActionResult Musica(int id, int round = 1)
        {
            Cenario cenario = _db.Cenario
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == id && !x.Excluido);

            if (cenario == null)
                return HttpNotFound("Cenario nao encontrado.");

            string stagesPath = MugenPaths.GetStagesPath();
            string arquivoMusica = MugenStageMusic.ObterArquivo(stagesPath, cenario.ArquivoDef, round);
            if (string.IsNullOrWhiteSpace(arquivoMusica))
                return HttpNotFound("Musica do cenario nao encontrada.");

            string caminhoMusica = Path.Combine(stagesPath, arquivoMusica);
            Response.Cache.SetCacheability(HttpCacheability.Private);
            Response.Cache.SetMaxAge(TimeSpan.FromMinutes(10));
            return File(caminhoMusica, MugenStageMusic.ObterContentType(arquivoMusica));
        }

        [HttpPost]
        public JsonResult AtualizarMusica(int id, int round)
        {
            try
            {
                Cenario cenario = _db.Cenario.FirstOrDefault(x => x.Id == id && !x.Excluido);
                if (cenario == null)
                    return Json(new { sucesso = false, mensagem = "Cenario nao encontrado." }, JsonRequestBehavior.DenyGet);
                if (round < 1 || round > 99)
                    return Json(new { sucesso = false, mensagem = "Informe um round entre 1 e 99." }, JsonRequestBehavior.DenyGet);

                HttpPostedFileBase arquivo = Request.Files["musica"];
                if (arquivo == null || arquivo.ContentLength <= 0 || !MugenStageMusic.EhArquivoDeMusica(arquivo.FileName))
                    return Json(new { sucesso = false, mensagem = "Selecione uma musica MP3, OGG, WAV ou MID." }, JsonRequestBehavior.DenyGet);
                if (arquivo.ContentLength > 32 * 1024 * 1024)
                    return Json(new { sucesso = false, mensagem = "A musica deve ter no maximo 32 MB." }, JsonRequestBehavior.DenyGet);

                string stagesPath = MugenPaths.GetStagesPath();
                string referencia = MugenStageMusic.ObterNomeDaFaixa(
                    cenario.ArquivoDef, arquivo.FileName, round, Guid.NewGuid().ToString("N").Substring(0, 12));
                List<StageMusicLocation> locais = ObterLocaisMusica(cenario, stagesPath);
                List<string> caminhosDef = locais.Select(x => x.DefPath).ToList();
                List<string> caminhosMusica = new List<string>();
                foreach (StageMusicLocation local in locais)
                {
                    string musica = Path.GetFullPath(Path.Combine(local.StagesPath, referencia.Replace('/', Path.DirectorySeparatorChar)));
                    if (!musica.StartsWith(local.StagesPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Os arquivos deste cenario nao estao disponiveis nas pastas stages.");
                    caminhosMusica.Add(musica);
                }

                byte[] conteudo;
                using (MemoryStream buffer = new MemoryStream())
                {
                    arquivo.InputStream.CopyTo(buffer);
                    conteudo = buffer.ToArray();
                }

                Dictionary<string, byte[]> originais = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string caminho in caminhosDef.Concat(caminhosMusica).Distinct(StringComparer.OrdinalIgnoreCase))
                    originais[caminho] = System.IO.File.Exists(caminho) ? System.IO.File.ReadAllBytes(caminho) : null;

                try
                {
                    for (int i = 0; i < caminhosDef.Count; i++)
                    {
                        System.IO.File.WriteAllBytes(caminhosMusica[i], conteudo);
                        MugenStageMusic.CorrigirReferenciaNoDef(caminhosDef[i], referencia, round);
                    }

                    cenario.DataAtualizacao = DateTime.Now;
                    _db.SaveChanges();
                }
                catch
                {
                    foreach (KeyValuePair<string, byte[]> original in originais)
                    {
                        if (original.Value == null)
                        {
                            if (System.IO.File.Exists(original.Key)) System.IO.File.Delete(original.Key);
                        }
                        else
                            System.IO.File.WriteAllBytes(original.Key, original.Value);
                    }
                    throw;
                }

                return Json(new
                {
                    sucesso = true,
                    cenario = CenarioDTO.ToDTO(cenario, stagesPath),
                    mensagem = "Musica do round " + round + " atualizada."
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult TrocarMusicas(int id, int round, int outroRound)
        {
            return AlterarMusicaComOutroRound(id, round, outroRound, true);
        }

        [HttpPost]
        public JsonResult UsarMusicaDeOutroRound(int id, int round, int outroRound)
        {
            return AlterarMusicaComOutroRound(id, round, outroRound, false);
        }

        private JsonResult AlterarMusicaComOutroRound(int id, int round, int outroRound, bool trocar)
        {
            try
            {
                Cenario cenario = _db.Cenario.FirstOrDefault(x => x.Id == id && !x.Excluido);
                if (cenario == null)
                    return Json(new { sucesso = false, mensagem = "Cenario nao encontrado." }, JsonRequestBehavior.DenyGet);
                if (round < 1 || round > 99 || outroRound < 1 || outroRound > 99 || round == outroRound)
                    return Json(new { sucesso = false, mensagem = "Selecione dois rounds diferentes entre 1 e 99." }, JsonRequestBehavior.DenyGet);

                string stagesPath = MugenPaths.GetStagesPath();
                string primeira = MugenStageMusic.ObterArquivo(stagesPath, cenario.ArquivoDef, round);
                string segunda = MugenStageMusic.ObterArquivo(stagesPath, cenario.ArquivoDef, outroRound);
                if (segunda == null || (trocar && primeira == null))
                    return Json(new { sucesso = false, mensagem = "A musica selecionada nao esta disponivel para esta operacao." }, JsonRequestBehavior.DenyGet);

                List<StageMusicLocation> locais = ObterLocaisMusica(cenario, stagesPath);
                foreach (StageMusicLocation local in locais)
                {
                    if (MugenStageMusic.ObterArquivo(local.StagesPath, cenario.ArquivoDef, outroRound) == null
                        || !System.IO.File.Exists(Path.Combine(local.StagesPath, segunda))
                        || (trocar && (MugenStageMusic.ObterArquivo(local.StagesPath, cenario.ArquivoDef, round) == null
                            || !System.IO.File.Exists(Path.Combine(local.StagesPath, primeira)))))
                        throw new InvalidOperationException("Uma das musicas nao foi encontrada na pasta stages do IKEMEN.");
                }

                Dictionary<string, byte[]> originais = locais.ToDictionary(x => x.DefPath, x => System.IO.File.ReadAllBytes(x.DefPath), StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (StageMusicLocation local in locais)
                    {
                        MugenStageMusic.CorrigirReferenciaNoDef(local.DefPath, segunda, round);
                        if (trocar)
                            MugenStageMusic.CorrigirReferenciaNoDef(local.DefPath, primeira, outroRound);
                    }
                    cenario.DataAtualizacao = DateTime.Now;
                    _db.SaveChanges();
                }
                catch
                {
                    foreach (KeyValuePair<string, byte[]> original in originais)
                        System.IO.File.WriteAllBytes(original.Key, original.Value);
                    throw;
                }

                return Json(new
                {
                    sucesso = true,
                    cenario = CenarioDTO.ToDTO(cenario, stagesPath),
                    mensagem = trocar
                        ? "Musicas dos rounds " + round + " e " + outroRound + " trocadas."
                        : "Musica do round " + outroRound + " atribuida ao round " + round + "."
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        [HttpPost]
        public JsonResult RemoverMusica(int id, int round)
        {
            try
            {
                Cenario cenario = _db.Cenario.FirstOrDefault(x => x.Id == id && !x.Excluido);
                if (cenario == null)
                    return Json(new { sucesso = false, mensagem = "Cenario nao encontrado." }, JsonRequestBehavior.DenyGet);
                if (round < 1 || round > 99)
                    return Json(new { sucesso = false, mensagem = "Informe um round entre 1 e 99." }, JsonRequestBehavior.DenyGet);

                string stagesPath = MugenPaths.GetStagesPath();
                if (MugenStageMusic.ObterArquivo(stagesPath, cenario.ArquivoDef, round) == null)
                    return Json(new { sucesso = false, mensagem = "Este round nao tem musica atribuida." }, JsonRequestBehavior.DenyGet);

                List<StageMusicLocation> locais = ObterLocaisMusica(cenario, stagesPath);
                Dictionary<string, byte[]> originais = locais.ToDictionary(x => x.DefPath, x => System.IO.File.ReadAllBytes(x.DefPath), StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (StageMusicLocation local in locais)
                        MugenStageMusic.RemoverReferenciaNoDef(local.DefPath, round);
                    cenario.DataAtualizacao = DateTime.Now;
                    _db.SaveChanges();
                }
                catch
                {
                    foreach (KeyValuePair<string, byte[]> original in originais)
                        System.IO.File.WriteAllBytes(original.Key, original.Value);
                    throw;
                }

                return Json(new
                {
                    sucesso = true,
                    cenario = CenarioDTO.ToDTO(cenario, stagesPath),
                    mensagem = "Musica do round " + round + " removida da configuracao. O arquivo foi preservado."
                }, JsonRequestBehavior.DenyGet);
            }
            catch (Exception ex)
            {
                return Erro(ex);
            }
        }

        private static List<StageMusicLocation> ObterLocaisMusica(Cenario cenario, string stagesPath)
        {
            List<string> pastas = new List<string> { stagesPath };
            IkemenPilotConfiguration ikemen = IkemenPilotConfiguration.Load();
            if (ikemen.IsConfigured)
            {
                string pastaIkemen = Path.Combine(ikemen.RootPath, "stages");
                if (Directory.Exists(pastaIkemen)
                    && !pastas.Any(x => string.Equals(Path.GetFullPath(x), Path.GetFullPath(pastaIkemen), StringComparison.OrdinalIgnoreCase))
                    && System.IO.File.Exists(Path.Combine(pastaIkemen, cenario.ArquivoDef)))
                    pastas.Add(pastaIkemen);
            }

            List<StageMusicLocation> locais = new List<StageMusicLocation>();
            foreach (string pasta in pastas)
            {
                string raiz = Path.GetFullPath(pasta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string def = Path.GetFullPath(Path.Combine(pasta, cenario.ArquivoDef ?? string.Empty));
                if (!def.StartsWith(raiz, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(def))
                    throw new InvalidOperationException("O arquivo .def deste cenario nao esta disponivel nas pastas stages.");
                locais.Add(new StageMusicLocation { StagesPath = pasta, DefPath = def });
            }
            return locais;
        }

        private sealed class StageMusicLocation
        {
            public string StagesPath { get; set; }
            public string DefPath { get; set; }
        }

        private List<HttpPostedFileBase> ObterArquivosEnviados()
        {
            List<HttpPostedFileBase> arquivos = new List<HttpPostedFileBase>();
            for (int i = 0; i < Request.Files.Count; i++)
            {
                HttpPostedFileBase arquivo = Request.Files[i];
                if (arquivo != null && arquivo.ContentLength > 0 && !string.IsNullOrWhiteSpace(arquivo.FileName))
                    arquivos.Add(arquivo);
            }

            return arquivos;
        }

        private string ObterPastaTemporariaImportacao()
        {
            return Server.MapPath("~/App_Data/StageImports");
        }

        private static void ValidarArquivos(
            Dictionary<HttpPostedFileBase, string> arquivos,
            string stagesPath,
            bool sobrescrever)
        {
            if (arquivos.Count == 0)
                throw new InvalidOperationException("Selecione os arquivos do cenario.");

            long tamanhoTotal = arquivos.Keys.Sum(x => (long)x.ContentLength);
            if (tamanhoTotal > 128L * 1024L * 1024L)
                throw new InvalidOperationException("O conjunto de arquivos nao pode ultrapassar 128 MB.");

            HashSet<string> nomesOriginais = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> nomesDestino = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<HttpPostedFileBase, string> item in arquivos)
            {
                HttpPostedFileBase arquivo = item.Key;
                string fileName = Path.GetFileName(arquivo.FileName);
                if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, arquivo.FileName.Replace('/', Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Last(), StringComparison.Ordinal))
                    throw new InvalidOperationException("Nome de arquivo invalido.");

                if (!ExtensoesPermitidas.Contains(Path.GetExtension(fileName)))
                    throw new InvalidOperationException("Tipo de arquivo nao permitido: " + fileName);

                if (!nomesOriginais.Add(fileName))
                    throw new InvalidOperationException("O arquivo foi enviado mais de uma vez: " + fileName);

                string nomeDestino = Path.GetFileName(item.Value);
                if (string.IsNullOrWhiteSpace(nomeDestino) || !string.Equals(nomeDestino, item.Value, StringComparison.Ordinal))
                    throw new InvalidOperationException("Nome de arquivo de destino invalido.");

                if (!nomesDestino.Add(nomeDestino))
                    throw new InvalidOperationException("Mais de um arquivo seria salvo com o nome: " + nomeDestino);

                if (!sobrescrever && System.IO.File.Exists(Path.Combine(stagesPath, nomeDestino)))
                    throw new InvalidOperationException("O arquivo ja existe em stages: " + nomeDestino + ". Marque a opcao de sobrescrita para substitui-lo.");
            }
        }

        private static string NormalizarTexto(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim();
            if (normalized.Length > maxLength)
                throw new InvalidOperationException("O texto informado deve possuir no maximo " + maxLength + " caracteres.");

            return normalized;
        }

        private static string SanitizeFileName(string value)
        {
            StringBuilder builder = new StringBuilder();
            HashSet<char> invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            foreach (char c in value ?? string.Empty)
            {
                if (!invalid.Contains(c) && !char.IsControl(c))
                    builder.Append(c);
            }

            string result = builder.ToString().Trim().Replace(' ', '-');
            return string.IsNullOrWhiteSpace(result) ? "stage" : result;
        }

        private JsonResult Erro(Exception ex)
        {
            string mensagem = ex.Message;
            if (mensagem.IndexOf("Cenario", StringComparison.OrdinalIgnoreCase) >= 0
                && mensagem.IndexOf("Invalid object name", StringComparison.OrdinalIgnoreCase) >= 0)
                mensagem = "A tabela de cenarios ainda nao existe. Execute App_Data/Migrations/20260813_AddCenario.sql no banco lutas.";
            else if (mensagem.IndexOf("CenasJson", StringComparison.OrdinalIgnoreCase) >= 0)
                mensagem = "A configuracao de cenas ainda nao existe no banco. Execute App_Data/Migrations/20260820_AddCenarioCenas.sql.";

            return Json(new { sucesso = false, mensagem }, JsonRequestBehavior.DenyGet);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _db.Dispose();

            base.Dispose(disposing);
        }
    }
}

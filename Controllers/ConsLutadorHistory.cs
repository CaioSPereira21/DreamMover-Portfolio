using DreamMover.Models;
using DreamMover.Services;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace DreamMover.Controllers
{
    public partial class ConsLutadorController
    {
        private IDisposable _historyEditLock;
        private List<int> _historyTargets;
        private bool _historyAllCharacters;
        private List<PersonagemCharCadastro> _historyDefinitions;
        private string HistoryRoot { get { return Server.MapPath("~/App_Data/CharacterHistory"); } }

        protected override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            if (context.Result != null) return;
            string action = context.ActionDescriptor.ActionName;
            var individual = new[] { "AtualizarDetalhesLutador", "AtualizarUniverso", "ImportarStand", "ImportarPortrait" };
            var collective = new[] { "ImportarStands", "ImportarStandDaPasta", "ImportarPacoteChar", "CadastrarPersonagensDosChars" };
            if (!individual.Contains(action) && !collective.Contains(action)) return;
            try
            {
                _historyEditLock = CharacterHistoryService.LockEdits(HistoryRoot);
                _historyAllCharacters = collective.Contains(action);
                int id = context.ActionParameters.ContainsKey("id") ? Convert.ToInt32(context.ActionParameters["id"]) : 0;
                var query = _db.Lutador.Include(x => x.Tag);
                var fighters = (_historyAllCharacters ? query : query.Where(x => x.Id == id)).ToList();
                _historyTargets = fighters.Select(x => x.Id).ToList();
                foreach (var fighter in fighters)
                    RecordCharacter(fighter, "Alteração externa detectada antes da edição", "Origem não identificada");
            }
            catch (Exception ex)
            {
                ReleaseHistoryLock();
                System.Diagnostics.Trace.TraceError("Preparação do histórico: " + ex);
                context.Result = Json(new { sucesso = false, mensagem = "Não foi possível preservar o histórico. A edição não foi iniciada." });
            }
        }

        protected override void OnActionExecuted(ActionExecutedContext context)
        {
            try
            {
                if (_historyEditLock != null && _historyTargets != null)
                {
                    bool failed = context.Exception != null;
                    var json = context.Result as JsonResult;
                    if (json != null && json.Data != null)
                    {
                        var property = json.Data.GetType().GetProperty("sucesso");
                        if (property != null) failed = failed || !Convert.ToBoolean(property.GetValue(json.Data, null));
                    }
                    string action = context.ActionDescriptor.ActionName;
                    string origin = failed ? "Alteração parcial detectada após falha" : HistoryOrigin(action);
                    // A fresh context records what was actually committed, even after a failed action.
                    using (var persisted = new lutasEntities())
                    {
                        var query = persisted.Lutador.Include(x => x.Tag);
                        var fighters = (_historyAllCharacters ? query : query.Where(x => _historyTargets.Contains(x.Id))).ToList();
                        _historyDefinitions = null;
                        foreach (var fighter in fighters) RecordCharacter(fighter, origin, HistoryUser());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Gravação do histórico após edição: " + ex);
                context.Result = Json(new { sucesso = false, mensagem = "A operação foi executada, mas houve falha ao finalizar o histórico. Reabra os detalhes para conferir o estado salvo antes de tentar novamente." });
            }
            finally { ReleaseHistoryLock(); }
            base.OnActionExecuted(context);
        }

        private void ReleaseHistoryLock()
        {
            if (_historyEditLock != null) { _historyEditLock.Dispose(); _historyEditLock = null; }
        }

        private string HistoryUser()
        {
            return User != null && User.Identity != null && User.Identity.IsAuthenticated
                ? User.Identity.Name : "Usuário local";
        }

        private static string HistoryOrigin(string action)
        {
            switch (action)
            {
                case "AtualizarDetalhesLutador": return "Edição do cadastro";
                case "AtualizarUniverso": return "Alteração de universo";
                case "ImportarPortrait": return "Importação de retrato";
                case "ImportarStand": case "ImportarStands": case "ImportarStandDaPasta": return "Importação de imagem de pose";
                case "ImportarPacoteChar": return "Importação de pacote do personagem";
                default: return "Cadastro de personagem";
            }
        }

        private CharacterHistoryDocument RecordCharacter(Lutador fighter, string origin, string responsible)
        {
            var previous = CharacterHistoryService.Read(HistoryRoot, fighter.Id);
            var snapshot = new CharacterHistorySnapshot { Id = fighter.Id };
            CharacterHistoryService.Set(snapshot, "Nome", fighter.Nome);
            CharacterHistoryService.Set(snapshot, "Autor", fighter.Autor);
            CharacterHistoryService.Set(snapshot, "Universo", fighter.Universo);
            CharacterHistoryService.Set(snapshot, "Tags", string.Join(", ", fighter.Tag.Select(x => x.Nome).OrderBy(x => x)));
            string imageRoot = Server.MapPath("~/Imagens");
            CharacterHistoryService.CaptureImage(HistoryRoot, snapshot, "Retrato", imageRoot, fighter.Portrait);
            CharacterHistoryService.CaptureImage(HistoryRoot, snapshot, "Pose", imageRoot, fighter.Stand);
            string defPath = null;
            try
            {
                string chars = ObterCaminhoCharsConfigurado();
                string previousDef = previous.Atual == null ? null : previous.Atual.DefPath;
                if (previousDef != null && System.IO.File.Exists(previousDef)
                    && Path.GetFullPath(previousDef).StartsWith(Path.GetFullPath(chars).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    defPath = previousDef;
                else
                {
                    if (_historyDefinitions == null)
                        _historyDefinitions = ObterDefsPrincipaisDosChars(chars).Select(x => LerPersonagemChar(x, chars)).ToList();
                    var character = LocalizarPersonagemChar(_historyDefinitions, fighter);
                    defPath = character == null ? null : character.DefPath;
                }
            }
            catch (IOException) { snapshot.Avisos.Add("A pasta de personagens está indisponível."); }
            CharacterHistoryService.CaptureDefinition(snapshot, defPath);
            return CharacterHistoryService.Record(HistoryRoot, snapshot, origin, responsible);
        }

        [HttpPost]
        public JsonResult GetHistoricoAlteracoes(int id, int pagina = 1, string filtro = "todos", string busca = "", string ate = null, bool atualizar = true)
        {
            try
            {
                CharacterHistoryDocument document;
                using (CharacterHistoryService.LockEdits(HistoryRoot))
                {
                    var fighter = _db.Lutador.Include(x => x.Tag).FirstOrDefault(x => x.Id == id);
                    if (fighter == null) return Json(new { sucesso = false, mensagem = "Lutador não encontrado." });
                    // Paging/search reads the saved journal; opening or refreshing checks the game files.
                    document = atualizar && pagina <= 1 ? null : CharacterHistoryService.Read(HistoryRoot, id);
                    if (document == null || document.Atual == null)
                        document = RecordCharacter(fighter, "Alteração externa detectada ao consultar", "Origem não identificada");
                }
                const int size = 20;
                if (atualizar && pagina <= 1) ate = null;
                ate = string.IsNullOrWhiteSpace(ate) ? document.Eventos.Last().Id : ate;
                var filtered = CharacterHistoryService.FilterEvents(document, filtro, busca, ate);
                pagina = Math.Max(1, Math.Min(pagina, Math.Max(1, (filtered.Count + size - 1) / size)));
                return Json(new
                {
                    sucesso = true,
                    eventos = filtered.Skip((pagina - 1) * size).Take(size).ToList(),
                    total = filtered.Count,
                    totalGeral = document.Eventos.Count,
                    ate,
                    pagina,
                    temMais = (long)pagina * size < filtered.Count,
                    avisos = document.Atual.Avisos,
                    ultimaBuscaAtualizacoes = document.UltimaBuscaAtualizacoes
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Consulta de histórico: " + ex);
                return Json(new { sucesso = false, mensagem = "Não foi possível carregar o histórico. Tente novamente." });
            }
        }

        [HttpPost]
        public JsonResult RegistrarBuscaAtualizacoes(int id, string dataBusca, string observacao, string fonte)
        {
            try
            {
                DateTime data;
                if (!DateTime.TryParseExact(dataBusca ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out data))
                    return Json(new { sucesso = false, mensagem = "Informe uma data válida para a busca." });
                if (data.Date > DateTime.Today)
                    return Json(new { sucesso = false, mensagem = "A data da busca não pode estar no futuro." });
                if ((observacao ?? string.Empty).Length > 2000 || (fonte ?? string.Empty).Length > 1000)
                    return Json(new { sucesso = false, mensagem = "A observação deve ter até 2.000 caracteres e a fonte até 1.000." });

                using (CharacterHistoryService.LockEdits(HistoryRoot))
                {
                    var fighter = _db.Lutador.Include(x => x.Tag).FirstOrDefault(x => x.Id == id);
                    if (fighter == null) return Json(new { sucesso = false, mensagem = "Lutador não encontrado." });
                    RecordCharacter(fighter, "Alteração externa detectada antes da pesquisa", "Origem não identificada");
                    var registro = CharacterHistoryService.RecordUpdateSearch(HistoryRoot, id, data.ToString("yyyy-MM-dd"), observacao, fonte, HistoryUser());
                    return Json(new { sucesso = true, ultimaBuscaAtualizacoes = registro });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Registro da busca de atualizações: " + ex);
                return Json(new { sucesso = false, mensagem = "Não foi possível registrar a busca de atualizações." });
            }
        }

        [HttpPost]
        public JsonResult InicializarHistoricoAlteracoes()
        {
            try
            {
                int count = 0;
                using (CharacterHistoryService.LockEdits(HistoryRoot))
                    foreach (var fighter in _db.Lutador.Include(x => x.Tag).OrderBy(x => x.Id).ToList())
                    {
                        RecordCharacter(fighter, "Alteração externa detectada na conferência", "Registro do estado atual");
                        count++;
                    }
                return Json(new { sucesso = true, personagens = count });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Inicialização de histórico: " + ex);
                return Json(new { sucesso = false, mensagem = "Não foi possível concluir o registro inicial. Os registros concluídos foram preservados." });
            }
        }

        [HttpGet]
        public ActionResult ImagemHistorico(int id, string arquivo)
        {
            try
            {
                string path = CharacterHistoryService.ImagePath(HistoryRoot, id, arquivo);
                if (!System.IO.File.Exists(path)) return HttpNotFound();
                return File(path, MimeMapping.GetMimeMapping(path));
            }
            catch (ArgumentException) { return HttpNotFound(); }
        }
    }
}

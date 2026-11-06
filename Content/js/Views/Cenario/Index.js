var appCenario = new Vue({
    el: "#cenarioApp",
    data: {
        Cenarios: [],
        Universos: [],
        Carregando: false,
        Sincronizando: false,
        Salvando: false,
        SalvandoMusica: false,
        RoundMusica: 1,
        RoundMusicaOrigem: null,
        ArquivoNovaMusica: null,
        TrocaMusica: {},
        CarregandoCenas: false,
        AnalisandoPacotes: false,
        CadastroAberto: false,
        GerandoImagemId: null,
        LeitorDef: {
            Carregando: false,
            CenarioId: null,
            Nome: "",
            ArquivoDef: "",
            Conteudo: "",
            Erro: ""
        },
        PlayerMusica: null,
        MusicaTocandoId: null,
        MusicaCarregandoId: null,
        GeracaoAutomatica: {
            Visivel: false,
            Ativa: false,
            CancelarSolicitado: false,
            Status: "ociosa",
            Fila: [],
            Total: 0,
            Processados: 0,
            Sucessos: 0,
            Falhas: 0,
            Atual: "",
            UltimoErro: ""
        },
        FiltroBusca: "",
        FiltroUniverso: "",
        FiltroStatus: "ativos",
        PaginaAtual: 1,
        ItensPorPagina: 18,
        ArquivosSelecionados: [],
        AnaliseToken: "",
        PacotesImportacao: [],
        CenariosDisponiveisRoteiro: [],
        Formulario: {
            Id: null,
            Nome: "",
            Universo: "",
            Autor: "",
            Ativo: true,
            SobrescreverArquivos: false,
            FaixasMusica: [],
            Cenas: []
        }
    },
    computed: {
        TotalAtivos: function () {
            return this.Cenarios.filter(function (item) { return item.Ativo && !item.Excluido && item.ArquivoEncontrado; }).length;
        },
        TotalClassificados: function () {
            return this.Cenarios.filter(function (item) { return !!item.Universo && !item.Excluido; }).length;
        },
        TotalComImagem: function () {
            return this.Cenarios.filter(function (item) { return !!item.Imagem && !item.Excluido; }).length;
        },
        TotalSemImagem: function () {
            return this.Cenarios.filter(function (item) {
                return !item.Imagem && !item.Excluido && item.ArquivoEncontrado;
            }).length;
        },
        ProgressoGeracaoAutomatica: function () {
            if (!this.GeracaoAutomatica.Total) return 0;
            return Math.min(100, Math.round((this.GeracaoAutomatica.Processados / this.GeracaoAutomatica.Total) * 100));
        },
        textoGeracaoAutomatica: function () {
            if (this.GeracaoAutomatica.Ativa && this.GeracaoAutomatica.CancelarSolicitado)
                return "Concluindo a captura atual antes de parar";
            if (this.GeracaoAutomatica.Ativa && this.GeracaoAutomatica.Atual)
                return "Capturando " + this.GeracaoAutomatica.Atual;
            if (this.GeracaoAutomatica.Status === "concluida")
                return "Geração de imagens concluída";
            if (this.GeracaoAutomatica.Status === "interrompida")
                return "Geração interrompida";
            return "Preparando a fila de cenários";
        },
        UniversosDoCatalogo: function () {
            var valores = this.Cenarios.map(function (item) { return item.Universo || ""; }).filter(Boolean);
            return Array.from(new Set(valores)).sort(function (a, b) { return a.localeCompare(b, "pt-BR"); });
        },
        CenariosFiltrados: function () {
            var self = this;
            var busca = this.normalizar(this.FiltroBusca);

            return this.Cenarios.filter(function (cenario) {
                var texto = self.normalizar([cenario.Nome, cenario.ArquivoDef, cenario.Autor].join(" "));
                if (busca && texto.indexOf(busca) < 0) return false;

                if (self.FiltroUniverso === "__sem_universo__" && cenario.Universo) return false;
                if (self.FiltroUniverso && self.FiltroUniverso !== "__sem_universo__" && cenario.Universo !== self.FiltroUniverso) return false;

                if (self.FiltroStatus === "ativos" && (!cenario.Ativo || cenario.Excluido || !cenario.ArquivoEncontrado)) return false;
                if (self.FiltroStatus === "inativos" && (cenario.Ativo || cenario.Excluido)) return false;
                if (self.FiltroStatus === "excluidos" && !cenario.Excluido) return false;
                if (self.FiltroStatus === "arquivo-ausente" && cenario.ArquivoEncontrado) return false;
                if (self.FiltroStatus === "sem-imagem" && (cenario.Imagem || cenario.Excluido || !cenario.ArquivoEncontrado)) return false;

                return true;
            }).sort(function (a, b) {
                return (a.Nome || "").localeCompare(b.Nome || "", "pt-BR");
            });
        },
        TotalPaginas: function () {
            return Math.max(1, Math.ceil(this.CenariosFiltrados.length / this.ItensPorPagina));
        },
        CenariosPaginados: function () {
            var inicio = (this.PaginaAtual - 1) * this.ItensPorPagina;
            return this.CenariosFiltrados.slice(inicio, inicio + this.ItensPorPagina);
        },
        TemFiltros: function () {
            return !!this.FiltroBusca || !!this.FiltroUniverso || this.FiltroStatus !== "ativos";
        },
        PacotesImportacaoAtivos: function () {
            return this.PacotesImportacao.filter(function (pacote) { return pacote.Importar; });
        },
        PodeConfirmarCadastro: function () {
            var self = this;
            if (this.Formulario.Id) return !!this.Formulario.Nome && this.cenasFormularioValidas();
            if (!this.AnaliseToken) return this.ArquivosSelecionados.length > 0 && !this.AnalisandoPacotes;
            if (!this.PacotesImportacaoAtivos.length) return false;
            return this.PacotesImportacaoAtivos.every(function (pacote) {
                return self.arquivosSelecionadosPorTipo(pacote, "def").length === 1
                    && self.quantidadeMusicasSelecionadas(pacote) === 1;
            });
        },
        TextoAcaoCadastro: function () {
            if (this.Formulario.Id) return "Salvar cenário";
            if (!this.AnaliseToken) return "Analisar pacotes";
            var quantidade = this.PacotesImportacaoAtivos.length;
            return "Importar " + quantidade + (quantidade === 1 ? " cenário" : " cenários");
        },
        MostrarMetadadosCadastro: function () {
            return !!this.Formulario.Id || (!!this.AnaliseToken && this.PacotesImportacaoAtivos.length === 1);
        }
    },
    methods: {
        carregar: function () {
            var self = this;
            self.Carregando = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/Listar",
                success: function (data) {
                    if (!data || !data.sucesso) {
                        toastr.error(data && data.mensagem ? data.mensagem : "Não foi possível carregar os cenários.");
                        return;
                    }

                    self.Cenarios = data.cenarios || [];
                    self.ajustarPagina();
                    if (data.importados > 0) toastr.success(data.importados + " cenário(s) importado(s) da pasta stages.");
                },
                error: function () { toastr.error("Não foi possível carregar os cenários."); },
                complete: function () { self.Carregando = false; }
            });
        },
        carregarUniversos: function () {
            var self = this;
            $.ajax({
                type: "POST",
                url: "/ConsLutador/GetUniversos",
                success: function (data) { self.Universos = data && data.universos ? data.universos : []; }
            });
        },
        sincronizar: function () {
            var self = this;
            if (self.Sincronizando) return;
            self.Sincronizando = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/Sincronizar",
                success: function (data) {
                    if (!data || !data.sucesso) {
                        toastr.error(data && data.mensagem ? data.mensagem : "Falha ao sincronizar stages.");
                        return;
                    }
                    toastr.success(data.mensagem);
                    self.carregar();
                },
                error: function () { toastr.error("Falha ao sincronizar stages."); },
                complete: function () { self.Sincronizando = false; }
            });
        },
        abrirCadastro: function () {
            this.cancelarImportacaoSilenciosa();
            this.Formulario = { Id: null, Nome: "", Universo: "", Autor: "", Ativo: true, SobrescreverArquivos: false, FaixasMusica: [], Cenas: [] };
            this.RoundMusica = 1;
            this.RoundMusicaOrigem = null;
            this.ArquivoNovaMusica = null;
            this.TrocaMusica = {};
            this.CenariosDisponiveisRoteiro = [];
            this.ArquivosSelecionados = [];
            this.PacotesImportacao = [];
            this.AnaliseToken = "";
            this.CadastroAberto = true;
            if (this.$refs.arquivosCenario) this.$refs.arquivosCenario.value = "";
            $("#modalCenario").modal("show");
        },
        abrirEdicao: function (cenario) {
            this.Formulario = {
                Id: cenario.Id,
                Nome: cenario.Nome || "",
                Universo: cenario.Universo || "",
                Autor: cenario.Autor || "",
                Ativo: !!cenario.Ativo,
                SobrescreverArquivos: false,
                FaixasMusica: (cenario.FaixasMusica || []).slice(),
                Cenas: this.prepararCenasFormulario(cenario.Cenas || [])
            };
            this.RoundMusica = 1;
            this.RoundMusicaOrigem = null;
            this.ArquivoNovaMusica = null;
            this.TrocaMusica = {};
            this.CenariosDisponiveisRoteiro = [];
            this.ArquivosSelecionados = [];
            this.PacotesImportacao = [];
            this.AnaliseToken = "";
            this.CadastroAberto = true;
            $("#modalCenario").modal("show");
            this.carregarConfiguracaoCenas(cenario.Id);
        },
        prepararCenasFormulario: function (cenas) {
            return (cenas || []).map(function (cena) {
                var transicao = cena.Transicao === "queda" ? "queda" : (cena.Transicao === "fade" ? "fade" : "corte");
                return {
                    Round: Number(cena.Round || 1),
                    CenarioNome: cena.CenarioNome || cena.Nome || "",
                    Transicao: transicao,
                    DuracaoFrames: Number(cena.DuracaoFrames || (transicao === "queda" ? 60 : 45))
                };
            }).sort(function (a, b) { return a.Round - b.Round; });
        },
        carregarConfiguracaoCenas: function (cenarioId) {
            var self = this;
            self.CarregandoCenas = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/ObterConfiguracaoCenas",
                data: { id: cenarioId },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        toastr.warning(data && data.mensagem ? data.mensagem : "Não foi possível carregar o catálogo de cenários.");
                        return;
                    }
                    if (self.Formulario.Id !== cenarioId) return;
                    self.CenariosDisponiveisRoteiro = data.cenariosDisponiveis || [];
                    self.Formulario.Cenas = self.prepararCenasFormulario(data.cenas || []);
                },
                error: function () { toastr.warning("Não foi possível carregar o catálogo de cenários."); },
                complete: function () { self.CarregandoCenas = false; }
            });
        },
        adicionarCena: function () {
            var cenas = this.Formulario.Cenas;
            if (!cenas.length) {
                cenas.push({ Round: 1, CenarioNome: this.Formulario.Nome || "", Transicao: "corte", DuracaoFrames: 0 });
                cenas.push({ Round: 2, CenarioNome: "", Transicao: "fade", DuracaoFrames: 45 });
                return;
            }

            var proximoRound = Math.max.apply(null, cenas.map(function (cena) { return Number(cena.Round || 0); })) + 1;
            cenas.push({
                Round: proximoRound,
                CenarioNome: "",
                Transicao: "fade",
                DuracaoFrames: 45
            });
        },
        removerCena: function (indice) {
            this.Formulario.Cenas.splice(indice, 1);
        },
        cenasFormularioValidas: function () {
            var cenas = this.Formulario.Cenas || [];
            if (!cenas.length) return true;
            if (cenas.length < 2 || cenas.length > 10) return false;

            var rounds = {};
            var ordenadas = cenas.slice().sort(function (a, b) { return Number(a.Round) - Number(b.Round); });
            if (Number(ordenadas[0].Round) !== 1) return false;

            return ordenadas.every(function (cena) {
                var round = Number(cena.Round);
                if (!Number.isInteger(round) || round < 1 || round > 99 || rounds[round]) return false;
                rounds[round] = true;

                if (!String(cena.CenarioNome || "").trim()) return false;

                return cena.Transicao !== "fade" && cena.Transicao !== "queda"
                    || (Number(cena.DuracaoFrames) >= 1 && Number(cena.DuracaoFrames) <= 180);
            });
        },
        serializarCenas: function () {
            return JSON.stringify((this.Formulario.Cenas || []).map(function (cena) {
                var transicao = cena.Transicao === "queda" ? "queda" : (cena.Transicao === "fade" ? "fade" : "corte");
                return {
                    Round: Number(cena.Round),
                    CenarioNome: String(cena.CenarioNome || "").trim(),
                    Transicao: transicao,
                    DuracaoFrames: transicao === "fade" || transicao === "queda"
                        ? Number(cena.DuracaoFrames || (transicao === "queda" ? 60 : 45))
                        : 0
                };
            }));
        },
        selecionarArquivos: function (event) {
            this.cancelarImportacaoSilenciosa();
            this.ArquivosSelecionados = event && event.target ? Array.prototype.slice.call(event.target.files || []) : [];
            this.PacotesImportacao = [];
            var invalido = this.ArquivosSelecionados.some(function (arquivo) {
                return !/\.(zip|rar)$/i.test(arquivo.name || "");
            });
            if (invalido) toastr.warning("Selecione somente pacotes .zip ou .rar.");
        },
        salvar: function () {
            if (this.Salvando) return;
            if (this.Formulario.Id) this.atualizar();
            else this.cadastrar();
        },
        atualizar: function () {
            var self = this;
            if (!self.Formulario.Nome) {
                toastr.warning("Informe o nome do cenário.");
                return;
            }
            if (!self.cenasFormularioValidas()) {
                toastr.warning("Revise o roteiro: selecione ao menos dois cenários e use rounds únicos.");
                return;
            }
            self.Salvando = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/Atualizar",
                data: {
                    id: self.Formulario.Id,
                    nome: self.Formulario.Nome,
                    universo: self.Formulario.Universo,
                    autor: self.Formulario.Autor,
                    ativo: self.Formulario.Ativo,
                    cenas: self.serializarCenas()
                },
                success: function (data) { self.concluirSalvamento(data); },
                error: function () { toastr.error("Não foi possível atualizar o cenário."); },
                complete: function () { self.Salvando = false; }
            });
        },
        cadastrar: function () {
            if (this.AnaliseToken) this.importarPacotes();
            else this.analisarPacotes();
        },
        analisarPacotes: function () {
            var self = this;
            if (!self.ArquivosSelecionados.length) {
                toastr.warning("Selecione ao menos um pacote .zip ou .rar.");
                return;
            }

            if (self.ArquivosSelecionados.some(function (arquivo) { return !/\.(zip|rar)$/i.test(arquivo.name || ""); })) {
                toastr.warning("Selecione somente pacotes .zip ou .rar.");
                return;
            }

            var formulario = new FormData();
            self.ArquivosSelecionados.forEach(function (arquivo) { formulario.append("arquivos", arquivo); });

            self.AnalisandoPacotes = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/AnalisarPacotes",
                data: formulario,
                processData: false,
                contentType: false,
                success: function (data) {
                    if (!data || !data.sucesso) {
                        toastr.error(data && data.mensagem ? data.mensagem : "Não foi possível analisar os pacotes.");
                        return;
                    }

                    if (!self.CadastroAberto) {
                        $.post("/Cenario/CancelarImportacao", { token: data.token });
                        return;
                    }

                    self.AnaliseToken = data.token || "";
                    self.PacotesImportacao = (data.pacotes || []).map(function (pacote) {
                        self.$set(pacote, "Importar", true);
                        self.$set(pacote, "MusicaAdicional", null);
                        return pacote;
                    });
                    toastr.info("Confira os arquivos identificados e escolha o que deseja importar.");
                },
                error: function (xhr) { toastr.error(self.mensagemErroAjax(xhr, "Não foi possível analisar os pacotes.")); },
                complete: function () { self.AnalisandoPacotes = false; }
            });
        },
        importarPacotes: function () {
            var self = this;
            if (!self.PodeConfirmarCadastro) {
                var semMusica = self.PacotesImportacaoAtivos.find(function (pacote) {
                    return self.quantidadeMusicasSelecionadas(pacote) === 0;
                });
                if (semMusica)
                    toastr.warning("Adicione uma música para " + semMusica.Nome + " ou cancele a adição desse pacote.");
                else
                    toastr.warning("Revise a seleção: cada pacote precisa de um .def e uma música.");
                return;
            }

            var selecoes = self.PacotesImportacao.map(function (pacote) {
                return {
                    PacoteId: pacote.Id,
                    Importar: !!pacote.Importar,
                    Arquivos: (pacote.Arquivos || []).filter(function (arquivo) {
                        return arquivo.Suportado && arquivo.Selecionado;
                    }).map(function (arquivo) { return arquivo.Id; })
                };
            });
            var formulario = new FormData();
            formulario.append("token", self.AnaliseToken);
            formulario.append("selecoes", JSON.stringify(selecoes));
            formulario.append("nome", self.Formulario.Nome || "");
            formulario.append("universo", self.Formulario.Universo || "");
            formulario.append("autor", self.Formulario.Autor || "");
            formulario.append("sobrescreverArquivos", self.Formulario.SobrescreverArquivos);
            self.PacotesImportacaoAtivos.forEach(function (pacote) {
                if (pacote.MusicaAdicional) formulario.append("musica_" + pacote.Id, pacote.MusicaAdicional);
            });

            self.Salvando = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/ImportarPacotes",
                data: formulario,
                processData: false,
                contentType: false,
                success: function (data) {
                    if (data && data.musicaObrigatoria && data.pacoteId) {
                        var pacote = self.PacotesImportacao.find(function (item) { return item.Id === data.pacoteId; });
                        if (pacote) self.$set(pacote, "ErroMusica", true);
                    }
                    self.concluirSalvamento(data);
                },
                error: function (xhr) { toastr.error(self.mensagemErroAjax(xhr, "Não foi possível importar os cenários.")); },
                complete: function () { self.Salvando = false; }
            });
        },
        selecionarArquivoPacote: function (pacote, arquivo, event) {
            if (!arquivo.Selecionado || (arquivo.Tipo !== "def" && arquivo.Tipo !== "musica")) return;
            (pacote.Arquivos || []).forEach(function (item) {
                if (item.Id !== arquivo.Id && item.Tipo === arquivo.Tipo) item.Selecionado = false;
            });
            if (arquivo.Tipo === "musica") {
                pacote.MusicaAdicional = null;
                pacote.ErroMusica = false;
                var card = event && event.target && event.target.closest ? event.target.closest(".stage-package") : null;
                var inputMusica = card ? card.querySelector(".stage-package__music input[type=file]") : null;
                if (inputMusica) inputMusica.value = "";
            }
        },
        selecionarMusicaAdicional: function (pacote, event) {
            var arquivo = event && event.target && event.target.files ? event.target.files[0] : null;
            this.$set(pacote, "MusicaAdicional", arquivo || null);
            this.$set(pacote, "ErroMusica", false);
        },
        alternarImportacaoPacote: function (pacote) {
            pacote.Importar = !pacote.Importar;
        },
        arquivosSelecionadosPorTipo: function (pacote, tipo) {
            return (pacote.Arquivos || []).filter(function (arquivo) {
                return arquivo.Suportado && arquivo.Selecionado && arquivo.Tipo === tipo;
            });
        },
        quantidadeMusicasSelecionadas: function (pacote) {
            return this.arquivosSelecionadosPorTipo(pacote, "musica").length + (pacote.MusicaAdicional ? 1 : 0);
        },
        formatarTamanhoArquivo: function (bytes) {
            var tamanho = Number(bytes || 0);
            if (tamanho < 1024) return tamanho + " B";
            if (tamanho < 1024 * 1024) return (tamanho / 1024).toFixed(1) + " KB";
            return (tamanho / (1024 * 1024)).toFixed(1) + " MB";
        },
        selecionarNovaMusica: function (event) {
            this.ArquivoNovaMusica = event && event.target && event.target.files ? event.target.files[0] : null;
        },
        atualizarMusica: function () {
            var self = this;
            var arquivo = self.ArquivoNovaMusica;
            var round = Number(self.RoundMusica);
            if (self.SalvandoMusica || !self.Formulario.Id) return;
            if (!Number.isInteger(round) || round < 1 || round > 99) {
                toastr.warning("Informe um round entre 1 e 99.");
                return;
            }
            if (!arquivo || !/\.(mp3|ogg|wav|mid)$/i.test(arquivo.name)) {
                toastr.warning("Selecione uma música MP3, OGG, WAV ou MID.");
                return;
            }
            var dados = new FormData();
            dados.append("id", self.Formulario.Id);
            dados.append("round", round);
            dados.append("musica", arquivo);
            self.SalvandoMusica = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/AtualizarMusica",
                data: dados,
                processData: false,
                contentType: false,
                success: function (data) {
                    if (!self.aplicarRetornoMusica(data)) return;
                    self.ArquivoNovaMusica = null;
                    if (self.$refs.arquivoNovaMusica) self.$refs.arquivoNovaMusica.value = "";
                },
                error: function (xhr) { toastr.error(self.mensagemErroAjax(xhr, "Não foi possível atualizar a música.")); },
                complete: function () { self.SalvandoMusica = false; }
            });
        },
        trocarMusicas: function (faixa) {
            this.alterarMusicaComOutroRound(faixa, "TrocarMusicas");
        },
        usarMusicaDeOutroRound: function (faixa) {
            this.alterarMusicaComOutroRound(faixa, "UsarMusicaDeOutroRound");
        },
        usarMusicaNoRound: function () {
            var self = this;
            var round = Number(self.RoundMusica);
            var origem = Number(self.RoundMusicaOrigem);
            if (self.SalvandoMusica || !self.Formulario.Id) return;
            if (!Number.isInteger(round) || round < 1 || round > 99 || round === origem
                || !(self.Formulario.FaixasMusica || []).some(function (item) { return item.Round === origem; })) {
                toastr.warning("Escolha um round de destino e uma faixa existente de outro round.");
                return;
            }
            self.SalvandoMusica = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/UsarMusicaDeOutroRound",
                data: { id: self.Formulario.Id, round: round, outroRound: origem },
                success: function (data) { self.aplicarRetornoMusica(data); },
                error: function (xhr) { toastr.error(self.mensagemErroAjax(xhr, "Não foi possível atribuir a música.")); },
                complete: function () { self.SalvandoMusica = false; }
            });
        },
        alterarMusicaComOutroRound: function (faixa, acao) {
            var self = this;
            var outroRound = Number(self.TrocaMusica[faixa.Round]);
            if (self.SalvandoMusica || !self.Formulario.Id) return;
            if (!Number.isInteger(outroRound) || outroRound === faixa.Round
                || !(self.Formulario.FaixasMusica || []).some(function (item) { return item.Round === outroRound; })) {
                toastr.warning("Selecione outro round com música.");
                return;
            }
            self.SalvandoMusica = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/" + acao,
                data: { id: self.Formulario.Id, round: faixa.Round, outroRound: outroRound },
                success: function (data) { self.aplicarRetornoMusica(data); },
                error: function (xhr) { toastr.error(self.mensagemErroAjax(xhr, "Não foi possível alterar as músicas.")); },
                complete: function () { self.SalvandoMusica = false; }
            });
        },
        removerMusica: function (faixa) {
            var self = this;
            if (self.SalvandoMusica || !self.Formulario.Id) return;
            self.SalvandoMusica = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/RemoverMusica",
                data: { id: self.Formulario.Id, round: faixa.Round },
                success: function (data) { self.aplicarRetornoMusica(data); },
                error: function (xhr) { toastr.error(self.mensagemErroAjax(xhr, "Não foi possível remover a música.")); },
                complete: function () { self.SalvandoMusica = false; }
            });
        },
        aplicarRetornoMusica: function (data) {
            if (!data || !data.sucesso || !data.cenario) {
                toastr.error(data && data.mensagem ? data.mensagem : "Não foi possível alterar a música.");
                return false;
            }
            this.pararMusica();
            var indice = this.Cenarios.findIndex(function (item) { return item.Id === data.cenario.Id; });
            if (indice >= 0) this.$set(this.Cenarios, indice, data.cenario);
            if (this.Formulario.Id === data.cenario.Id)
                this.Formulario.FaixasMusica = (data.cenario.FaixasMusica || []).slice();
            this.TrocaMusica = {};
            this.RoundMusicaOrigem = null;
            toastr.success(data.mensagem);
            return true;
        },
        mensagemErroAjax: function (xhr, padrao) {
            return xhr && xhr.responseJSON && xhr.responseJSON.mensagem ? xhr.responseJSON.mensagem : padrao;
        },
        cancelarImportacaoSilenciosa: function () {
            var token = this.AnaliseToken;
            this.AnaliseToken = "";
            this.PacotesImportacao = [];
            if (token) $.post("/Cenario/CancelarImportacao", { token: token });
        },
        concluirSalvamento: function (data) {
            if (!data || !data.sucesso) {
                toastr.error(data && data.mensagem ? data.mensagem : "Não foi possível salvar o cenário.");
                return;
            }
            var self = this;
            var atualizados = data.cenarios || (data.cenario ? [data.cenario] : []);
            atualizados.forEach(function (cenarioAtualizado) {
                var indice = this.Cenarios.findIndex(function (item) { return item.Id === cenarioAtualizado.Id; });
                if (indice >= 0) this.$set(this.Cenarios, indice, cenarioAtualizado);
                else this.Cenarios.push(cenarioAtualizado);
            }, this);
            self.ajustarPagina();
            self.AnaliseToken = "";
            self.PacotesImportacao = [];
            toastr.success(data.mensagem || "Cenário salvo.");
            $("#modalCenario").modal("hide");
        },
        excluir: function (cenario) {
            var self = this;
            Swal.fire({
                title: "Excluir " + cenario.Nome + "?",
                text: "Ele sairá da rotação, mas os arquivos físicos serão preservados e o cadastro poderá ser restaurado.",
                icon: "warning",
                showCancelButton: true,
                confirmButtonText: "Excluir do DreamMover",
                cancelButtonText: "Cancelar",
                confirmButtonColor: "#dc2626"
            }).then(function (resultado) {
                if (!resultado.isConfirmed) return;
                self.postAcao("/Cenario/Excluir", cenario, "Excluindo cenário...");
            });
        },
        restaurar: function (cenario) {
            this.postAcao("/Cenario/Restaurar", cenario, "Restaurando cenário...");
        },
        lerDef: function (cenario) {
            var self = this;
            if (!cenario || !cenario.ArquivoEncontrado || self.LeitorDef.Carregando) return;

            self.LeitorDef = {
                Carregando: true,
                CenarioId: cenario.Id,
                Nome: cenario.Nome || "Cenário",
                ArquivoDef: cenario.ArquivoDef || "",
                Conteudo: "",
                Erro: ""
            };
            $("#modalLeitorDef").modal("show");

            $.ajax({
                type: "POST",
                url: "/Cenario/LerDef",
                data: { id: cenario.Id },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        self.LeitorDef.Erro = data && data.mensagem ? data.mensagem : "Não foi possível ler o arquivo .def.";
                        return;
                    }

                    self.LeitorDef.Nome = data.nome || self.LeitorDef.Nome;
                    self.LeitorDef.ArquivoDef = data.arquivoDef || self.LeitorDef.ArquivoDef;
                    self.LeitorDef.Conteudo = data.conteudo || "";
                },
                error: function (xhr) {
                    self.LeitorDef.Erro = self.mensagemErroAjax(xhr, "Não foi possível ler o arquivo .def.");
                },
                complete: function () { self.LeitorDef.Carregando = false; }
            });
        },
        postAcao: function (url, cenario, mensagem) {
            var self = this;
            toastr.info(mensagem);
            $.ajax({
                type: "POST",
                url: url,
                data: { id: cenario.Id },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        toastr.error(data && data.mensagem ? data.mensagem : "A operação não foi concluída.");
                        return;
                    }
                    toastr.success(data.mensagem);
                    self.carregar();
                },
                error: function () { toastr.error("A operação não foi concluída."); }
            });
        },
        iniciarGeracaoAutomatica: function () {
            var self = this;
            if (self.GeracaoAutomatica.Ativa || self.GerandoImagemId !== null) return;

            var pendentes = self.Cenarios.filter(function (cenario) {
                return !cenario.Imagem && !cenario.Excluido && cenario.ArquivoEncontrado;
            });

            if (!pendentes.length) {
                toastr.info("Todos os cenários disponíveis já possuem imagem.");
                return;
            }

            Swal.fire({
                title: pendentes.length === 1 ? "Gerar 1 imagem?" : "Gerar " + pendentes.length + " imagens?",
                text: "Os cenários serão capturados um por vez. Mantenha esta página aberta durante o processo.",
                icon: "question",
                showCancelButton: true,
                confirmButtonText: "Iniciar geração",
                cancelButtonText: "Cancelar",
                confirmButtonColor: "#6f42c1"
            }).then(function (resultado) {
                if (!resultado.isConfirmed) return;

                self.GeracaoAutomatica = {
                    Visivel: true,
                    Ativa: true,
                    CancelarSolicitado: false,
                    Status: "executando",
                    Fila: pendentes.slice(),
                    Total: pendentes.length,
                    Processados: 0,
                    Sucessos: 0,
                    Falhas: 0,
                    Atual: "",
                    UltimoErro: ""
                };
                self.processarProximaImagem();
            });
        },
        processarProximaImagem: function () {
            var self = this;
            if (!self.GeracaoAutomatica.Ativa) return;

            if (self.GeracaoAutomatica.CancelarSolicitado || !self.GeracaoAutomatica.Fila.length) {
                self.finalizarGeracaoAutomatica(self.GeracaoAutomatica.CancelarSolicitado);
                return;
            }

            var cenario = self.GeracaoAutomatica.Fila.shift();
            self.GeracaoAutomatica.Atual = cenario.Nome;
            self.GerandoImagemId = cenario.Id;

            $.ajax({
                type: "POST",
                url: "/Cenario/GerarImagem",
                data: { id: cenario.Id },
                timeout: 55000
            }).done(function (data) {
                if (!data || !data.sucesso) {
                    self.registrarFalhaAutomatica(cenario, data && data.mensagem ? data.mensagem : "O sistema não retornou a imagem.");
                    return;
                }

                self.$set(cenario, "Imagem", data.imagem + "?v=" + new Date().getTime());
                self.GeracaoAutomatica.Sucessos++;
            }).fail(function (xhr, status) {
                self.registrarFalhaAutomatica(cenario, self.obterMensagemFalhaImagem(xhr, status));
            }).always(function () {
                self.GerandoImagemId = null;
                self.GeracaoAutomatica.Processados++;
                window.setTimeout(function () { self.processarProximaImagem(); }, 350);
            });
        },
        cancelarGeracaoAutomatica: function () {
            if (!this.GeracaoAutomatica.Ativa || this.GeracaoAutomatica.CancelarSolicitado) return;
            this.GeracaoAutomatica.CancelarSolicitado = true;
            toastr.info("A geração será interrompida após a captura atual.");
        },
        finalizarGeracaoAutomatica: function (interrompida) {
            var geracao = this.GeracaoAutomatica;
            geracao.Ativa = false;
            geracao.Status = interrompida ? "interrompida" : "concluida";
            geracao.Atual = "";
            geracao.Fila = [];

            if (interrompida)
                toastr.info("Geração automática interrompida.");
            else if (geracao.Falhas > 0)
                toastr.warning("Geração concluída com " + geracao.Falhas + " falha" + (geracao.Falhas === 1 ? "" : "s") + ".");
            else
                toastr.success("Todas as imagens pendentes foram geradas.");
        },
        fecharResumoGeracao: function () {
            if (!this.GeracaoAutomatica.Ativa) this.GeracaoAutomatica.Visivel = false;
        },
        registrarFalhaAutomatica: function (cenario, mensagem) {
            this.GeracaoAutomatica.Falhas++;
            this.GeracaoAutomatica.UltimoErro = cenario.Nome + ": " + mensagem;
        },
        obterMensagemFalhaImagem: function (xhr, status) {
            if (status === "timeout") return "A captura excedeu o tempo limite.";
            if (xhr && xhr.responseJSON && xhr.responseJSON.mensagem) return xhr.responseJSON.mensagem;
            return "Não foi possível gerar a imagem.";
        },
        gerarImagem: function (cenario) {
            var self = this;
            if (self.GerandoImagemId !== null || self.GeracaoAutomatica.Ativa) return;
            self.GerandoImagemId = cenario.Id;
            toastr.info("O IKEMEN será aberto por alguns segundos para capturar o cenário.");
            $.ajax({
                type: "POST",
                url: "/Cenario/GerarImagem",
                data: { id: cenario.Id },
                timeout: 55000,
                success: function (data) {
                    if (!data || !data.sucesso) {
                        toastr.error(data && data.mensagem ? data.mensagem : "Não foi possível gerar a imagem.");
                        return;
                    }
                    cenario.Imagem = data.imagem + "?v=" + new Date().getTime();
                    toastr.success(data.mensagem);
                },
                error: function () { toastr.error("Não foi possível gerar a imagem do cenário."); },
                complete: function () { self.GerandoImagemId = null; }
            });
        },
        alternarMusica: function (cenario, faixa) {
            var self = this;
            if (!cenario || !faixa || !faixa.Url) return;
            var chave = cenario.Id + ":" + faixa.Round;

            if (self.MusicaTocandoId === chave) {
                self.pararMusica();
                return;
            }

            self.pararMusica();
            var player = new Audio(faixa.Url);
            self.PlayerMusica = player;
            self.MusicaTocandoId = chave;
            self.MusicaCarregandoId = chave;

            player.addEventListener("playing", function () {
                if (self.PlayerMusica === player) self.MusicaCarregandoId = null;
            });
            player.addEventListener("ended", function () {
                if (self.PlayerMusica === player) self.pararMusica();
            });
            player.addEventListener("error", function () {
                if (self.PlayerMusica !== player) return;
                self.pararMusica();
                toastr.error("Não foi possível reproduzir a música deste cenário.");
            });

            var reproducao = player.play();
            if (reproducao && typeof reproducao.catch === "function") {
                reproducao.catch(function () {
                    if (self.PlayerMusica !== player) return;
                    self.pararMusica();
                    toastr.error("O navegador não conseguiu iniciar a reprodução da música.");
                });
            }
        },
        pararMusica: function () {
            var player = this.PlayerMusica;
            this.PlayerMusica = null;
            this.MusicaTocandoId = null;
            this.MusicaCarregandoId = null;

            if (!player) return;
            player.pause();
            try { player.currentTime = 0; } catch (erro) { }
        },
        urlImagem: function (cenario) {
            return cenario.Imagem || "/Images/Icon-v2.png";
        },
        imagemComErro: function (cenario, event) {
            cenario.Imagem = null;
            if (event && event.target) event.target.style.display = "none";
        },
        logoUniversoComErro: function (cenario, event) {
            cenario.UniversoImagem = null;
            if (event && event.target) event.target.style.display = "none";
        },
        textoStatus: function (cenario) {
            if (cenario.Excluido) return "Excluído";
            if (!cenario.ArquivoEncontrado) return "Arquivo ausente";
            return cenario.Ativo ? "Em rotação" : "Inativo";
        },
        classeStatus: function (cenario) {
            if (cenario.Excluido || !cenario.ArquivoEncontrado) return "is-danger";
            return cenario.Ativo ? "is-active" : "is-inactive";
        },
        limparFiltros: function () {
            this.FiltroBusca = "";
            this.FiltroUniverso = "";
            this.FiltroStatus = "ativos";
            this.PaginaAtual = 1;
        },
        ajustarPagina: function () {
            if (this.PaginaAtual > this.TotalPaginas) this.PaginaAtual = this.TotalPaginas;
            if (this.PaginaAtual < 1) this.PaginaAtual = 1;
        },
        normalizar: function (valor) {
            return String(valor || "").normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
        },
        avisarSaidaDuranteGeracao: function (event) {
            if (!this.GeracaoAutomatica.Ativa) return;
            event.preventDefault();
            event.returnValue = "";
            return "";
        }
    },
    watch: {
        FiltroBusca: function () { this.PaginaAtual = 1; },
        FiltroUniverso: function () { this.PaginaAtual = 1; },
        FiltroStatus: function () { this.PaginaAtual = 1; }
    },
    mounted: function () {
        var self = this;
        this.carregar();
        this.carregarUniversos();
        window.addEventListener("beforeunload", this.avisarSaidaDuranteGeracao);
        $("#modalCenario").on("hidden.bs.modal.stageImport", function () {
            self.CadastroAberto = false;
            self.cancelarImportacaoSilenciosa();
        });
    },
    beforeDestroy: function () {
        this.pararMusica();
        this.cancelarImportacaoSilenciosa();
        $("#modalCenario").off("hidden.bs.modal.stageImport");
        window.removeEventListener("beforeunload", this.avisarSaidaDuranteGeracao);
    }
});

if (window.appDetalhesLutador && window.appDetalhesLutador.$destroy) {
    window.appDetalhesLutador.$destroy();
}

var appDetalhesLutador = new Vue({
    el: "#detalhesLutadorApp",
    data: {
        Lutador: {},
        NomeRival: "",
        ConquistasUniverso: [],
        CarregandoConquistas: false,
        TorreConquistaAbertaId: null,
        PontoEloAtivo: null,
        AbaDetalhes: "resumo",
        HistoricoAlteracoes: [],
        CarregandoHistorico: false,
        HistoricoCarregado: false,
        ErroHistorico: "",
        AvisosHistorico: [],
        PaginaHistorico: 0,
        TotalHistorico: 0,
        HistoricoTemMais: false,
        TotalGeralHistorico: 0,
        FiltroHistorico: "todos",
        BuscaHistorico: "",
        AncoraHistorico: null,
        UltimaConsultaHistorico: "",
        UltimaBuscaAtualizacoes: null,
        MostrarFormBuscaAtualizacoes: false,
        DataBuscaAtualizacoes: "",
        ObservacaoBuscaAtualizacoes: "",
        FonteBuscaAtualizacoes: "",
        SalvandoBuscaAtualizacoes: false,
        ErroBuscaAtualizacoes: "",
        HistoricoErroMais: false,
        HistoricoErroAtualizar: false,
        CategoriasHistorico: [
            { id: "todos", nome: "Tudo" }, { id: "atributos", nome: "Atributos" },
            { id: "imagens", nome: "Imagens" }, { id: "cadastro", nome: "Cadastro" },
            { id: "arquivos", nome: "Arquivos e IA" }
        ]
    },
    mounted: function () {
        var lutadorJson = document.getElementById("lutadorDetalhesJson");

        if (lutadorJson && lutadorJson.textContent) {
            this.Lutador = JSON.parse(lutadorJson.textContent);
            var historicoElo = this.Lutador.HistoricoRatingElo || [];
            this.PontoEloAtivo = historicoElo.length ? historicoElo[historicoElo.length - 1] : null;
            this.carregarConquistas();
        }
    },
    beforeDestroy: function () {
        clearTimeout(this._historyTimer);
        if (this._historyRequest) this._historyRequest.abort();
    },
    computed: {
        ultimasPartidas: function () {
            return this.Lutador && this.Lutador.UltimasPartidas ? this.Lutador.UltimasPartidas : [];
        },
        aproveitamento: function () {
            var partidas = this.Lutador.Partidas || 0;

            if (!partidas) {
                return 0;
            }

            return Math.round(((this.Lutador.Vitorias || 0) / partidas) * 100);
        },
        formaRecente: function () {
            var self = this;

            return this.ultimasPartidas.slice(0, 6).map(function (partida) {
                return partida.IdVencedor == self.Lutador.Id ? "V" : "D";
            });
        },
        melhorRival: function () {
            var rivais = this.getRivaisComHistorico();

            if (!rivais.length) {
                return null;
            }

            return rivais.sort(function (a, b) {
                var aproveitamentoB = b.Vitorias / (b.PartidasTotais || 1);
                var aproveitamentoA = a.Vitorias / (a.PartidasTotais || 1);
                return aproveitamentoB - aproveitamentoA || (b.Vitorias || 0) - (a.Vitorias || 0);
            })[0];
        },
        piorRival: function () {
            var rivais = this.getRivaisComHistorico();

            if (!rivais.length) {
                return null;
            }

            return rivais.sort(function (a, b) {
                var aproveitamentoA = a.Vitorias / (a.PartidasTotais || 1);
                var aproveitamentoB = b.Vitorias / (b.PartidasTotais || 1);
                return aproveitamentoA - aproveitamentoB || (b.Derrotas || 0) - (a.Derrotas || 0);
            })[0];
        },
        RivaisFiltrados: function () {
            var nomeRival = this.NomeRival.toLowerCase();

            if (!this.Lutador || !this.Lutador.Rivais) {
                return [];
            }

            return this.Lutador.Rivais.filter(function (rival) {
                return !nomeRival || (rival.Nome || "").toLowerCase().indexOf(nomeRival) >= 0;
            });
        },
        historicoRatingElo: function () {
            return this.Lutador && this.Lutador.HistoricoRatingElo
                ? this.Lutador.HistoricoRatingElo
                : [];
        },
        configuracaoGraficoElo: function () {
            var historico = this.historicoRatingElo;
            var ratingInicial = historico.length
                ? Number(historico[0].RatingEloAnterior || 1000)
                : Number(this.Lutador.RatingElo || 1000);
            var ratings = [ratingInicial].concat(historico.map(function (ponto) {
                return Number(ponto.RatingEloAtual || 1000);
            }));
            var menor = Math.min.apply(Math, ratings);
            var maior = Math.max.apply(Math, ratings);
            var margem = Math.max(24, Math.ceil((maior - menor) * 0.16));
            var minimo = Math.max(0, Math.floor((menor - margem) / 25) * 25);
            var maximo = Math.ceil((maior + margem) / 25) * 25;

            if (maximo <= minimo) {
                maximo = minimo + 50;
            }

            return {
                largura: Math.max(720, 112 + (historico.length * 92)),
                altura: 350,
                topo: 42,
                direita: 32,
                base: 82,
                esquerda: 58,
                minimo: minimo,
                maximo: maximo,
                ratingInicial: ratingInicial
            };
        },
        pontosGraficoElo: function () {
            var config = this.configuracaoGraficoElo;
            var historico = this.historicoRatingElo;
            var pontosOriginais = [{
                PartidaId: 0,
                Inicial: true,
                RatingEloAtual: config.ratingInicial,
                AdversarioNome: "Inicio"
            }].concat(historico.map(function (ponto) {
                return Object.assign({ Inicial: false }, ponto);
            }));
            var larguraUtil = config.largura - config.esquerda - config.direita;
            var alturaUtil = config.altura - config.topo - config.base;
            var intervaloX = pontosOriginais.length > 1
                ? larguraUtil / (pontosOriginais.length - 1)
                : 0;

            return pontosOriginais.map(function (ponto, indice) {
                var rating = Number(ponto.RatingEloAtual || config.ratingInicial);
                var proporcao = (rating - config.minimo) / (config.maximo - config.minimo);
                return Object.assign({}, ponto, {
                    x: config.esquerda + (intervaloX * indice),
                    y: config.topo + ((1 - proporcao) * alturaUtil)
                });
            });
        },
        linhasGradeElo: function () {
            var config = this.configuracaoGraficoElo;
            var linhas = [];
            var alturaUtil = config.altura - config.topo - config.base;

            for (var indice = 0; indice < 5; indice++) {
                var proporcao = indice / 4;
                linhas.push({
                    rating: Math.round(config.maximo - ((config.maximo - config.minimo) * proporcao)),
                    y: config.topo + (alturaUtil * proporcao)
                });
            }

            return linhas;
        },
        eloPolyline: function () {
            return this.pontosGraficoElo.map(function (ponto) {
                return ponto.x + "," + ponto.y;
            }).join(" ");
        },
        resumoHistoricoElo: function () {
            var config = this.configuracaoGraficoElo;
            var ratings = this.pontosGraficoElo.map(function (ponto) {
                return Number(ponto.RatingEloAtual || config.ratingInicial);
            });
            var ultimo = ratings.length ? ratings[ratings.length - 1] : config.ratingInicial;

            return {
                atual: ultimo,
                maior: Math.max.apply(Math, ratings),
                menor: Math.min.apply(Math, ratings),
                variacao: ultimo - config.ratingInicial
            };
        }
    },
    methods: {
        prepararEventoHistorico: function (evento, aberto) {
            var fields = evento.Campos || [];
            var arquivos = fields.filter(function (c) { return c.Campo.indexOf("Arquivo: ") === 0; }).map(function (c) {
                return Object.assign({}, c, { Nome: c.Campo.substring(9), Estado: !c.Atual ? "Referência removida" :
                    c.Atual === "Arquivo ausente" ? "Não localizado" : !c.Anterior ? "Adicionado" : "Alterado" });
            });
            var dados = fields.filter(function (c) { return c.Campo.indexOf("Arquivo: ") !== 0; });
            var partes = [];
            if (dados.length) partes.push(dados.slice(0, 3).map(function (c) { return c.Campo; }).join(", ") +
                (dados.length > 3 ? " e mais " + (dados.length - 3) : ""));
            if (arquivos.length) partes.push(arquivos.length + " arquivo" + (arquivos.length === 1 ? "" : "s"));
            return Object.assign({}, evento, { Dados: dados, Arquivos: arquivos, Resumo: partes.join(" · "),
                Aberto: aberto, ArquivosAbertos: false });
        },
        filtrarHistorico: function (filtro) {
            this.FiltroHistorico = filtro;
            this.carregarHistoricoAlteracoes(false, false);
        },
        buscarHistorico: function () {
            clearTimeout(this._historyTimer);
            // Invalidate an older search immediately, before the debounce interval.
            this._historySequence = (this._historySequence || 0) + 1;
            if (this._historyRequest) this._historyRequest.abort();
            this.CarregandoHistorico = true;
            var self = this;
            this._historyTimer = setTimeout(function () { self.carregarHistoricoAlteracoes(false, false); }, 300);
        },
        limparFiltrosHistorico: function () {
            this.BuscaHistorico = "";
            this.filtrarHistorico("todos");
        },
        abrirRegistroBuscaAtualizacoes: function () {
            var hoje = new Date();
            var mes = String(hoje.getMonth() + 1).padStart(2, "0");
            var dia = String(hoje.getDate()).padStart(2, "0");
            this.DataBuscaAtualizacoes = hoje.getFullYear() + "-" + mes + "-" + dia;
            this.ErroBuscaAtualizacoes = "";
            this.MostrarFormBuscaAtualizacoes = true;
        },
        cancelarRegistroBuscaAtualizacoes: function () {
            if (this.SalvandoBuscaAtualizacoes) return;
            this.MostrarFormBuscaAtualizacoes = false;
            this.ErroBuscaAtualizacoes = "";
        },
        fonteBuscaEhLink: function (fonte) {
            return /^https?:\/\//i.test(fonte || "");
        },
        formatarDataBuscaAtualizacoes: function (valor) {
            var partes = (valor || "").split("-");
            return partes.length === 3 ? partes[2] + "/" + partes[1] + "/" + partes[0] : (valor || "Não registrada");
        },
        registrarBuscaAtualizacoes: function () {
            if (!this.Lutador.Id || !this.DataBuscaAtualizacoes || this.SalvandoBuscaAtualizacoes) return;
            var self = this;
            this.SalvandoBuscaAtualizacoes = true;
            this.ErroBuscaAtualizacoes = "";
            $.ajax({
                type: "POST",
                url: "/ConsLutador/RegistrarBuscaAtualizacoes",
                timeout: 60000,
                data: { id: this.Lutador.Id, dataBusca: this.DataBuscaAtualizacoes,
                    observacao: this.ObservacaoBuscaAtualizacoes, fonte: this.FonteBuscaAtualizacoes },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        self.ErroBuscaAtualizacoes = data && data.mensagem ? data.mensagem : "Não foi possível registrar a busca.";
                        return;
                    }
                    self.UltimaBuscaAtualizacoes = data.ultimaBuscaAtualizacoes;
                    self.MostrarFormBuscaAtualizacoes = false;
                    self.UltimaConsultaHistorico = new Date().toLocaleTimeString("pt-BR");
                },
                error: function () { self.ErroBuscaAtualizacoes = "Não foi possível registrar a busca. Tente novamente."; },
                complete: function () { self.SalvandoBuscaAtualizacoes = false; }
            });
        },
        tentarHistorico: function () {
            this.carregarHistoricoAlteracoes(this.HistoricoErroMais, this.HistoricoErroAtualizar);
        },
        selecionarAbaDetalhes: function (aba) {
            this.AbaDetalhes = aba;
            if (aba === "historico" && !this.HistoricoCarregado) this.carregarHistoricoAlteracoes(false);
        },
        navegarAbasDetalhes: function (event) {
            if (["ArrowLeft", "ArrowRight", "Home", "End"].indexOf(event.key) < 0) return;
            event.preventDefault();
            var aba = event.key === "Home" ? "resumo" : event.key === "End" ? "historico" :
                this.AbaDetalhes === "resumo" ? "historico" : "resumo";
            this.selecionarAbaDetalhes(aba);
            this.$nextTick(function () { document.getElementById("fighter-tab-" + aba).focus(); });
        },
        carregarHistoricoAlteracoes: function (mais, atualizar) {
            if (!this.Lutador.Id || (mais && this.CarregandoHistorico)) return;
            clearTimeout(this._historyTimer);
            var sequence = this._historySequence = (this._historySequence || 0) + 1;
            if (this._historyRequest) this._historyRequest.abort();
            atualizar = atualizar === undefined ? !mais : atualizar;
            var self = this;
            var pagina = mais ? this.PaginaHistorico + 1 : 1;
            this.CarregandoHistorico = true;
            this.ErroHistorico = "";
            this.HistoricoErroMais = !!mais;
            this.HistoricoErroAtualizar = !!atualizar;
            this._historyRequest = $.ajax({
                type: "POST",
                url: "/ConsLutador/GetHistoricoAlteracoes",
                timeout: 60000,
                data: { id: this.Lutador.Id, pagina: pagina, filtro: this.FiltroHistorico,
                    busca: this.BuscaHistorico, ate: atualizar ? "" : this.AncoraHistorico, atualizar: atualizar },
                success: function (data) {
                    if (sequence !== self._historySequence) return;
                    if (!data || !data.sucesso) {
                        self.ErroHistorico = data && data.mensagem ? data.mensagem : "Não foi possível carregar o histórico.";
                        return;
                    }
                    var events = (data.eventos || []).map(function (e, i) {
                        var existing = self.HistoricoAlteracoes.filter(function (old) { return old.Id === e.Id; })[0];
                        return self.prepararEventoHistorico(e, existing ? existing.Aberto : !mais && i === 0 && !e.Inicial);
                    });
                    self.HistoricoAlteracoes = mais ? self.HistoricoAlteracoes.concat(events.filter(function (e) {
                        return !self.HistoricoAlteracoes.some(function (old) { return old.Id === e.Id; });
                    })) : events;
                    self.PaginaHistorico = data.pagina;
                    self.TotalHistorico = data.total;
                    self.TotalGeralHistorico = data.totalGeral;
                    self.AncoraHistorico = data.ate;
                    self.UltimaBuscaAtualizacoes = data.ultimaBuscaAtualizacoes || null;
                    if (atualizar || !self.UltimaConsultaHistorico) self.UltimaConsultaHistorico = new Date().toLocaleTimeString("pt-BR");
                    self.HistoricoTemMais = data.temMais;
                    self.AvisosHistorico = data.avisos || [];
                    self.HistoricoCarregado = true;
                },
                error: function (xhr, status) {
                    if (sequence === self._historySequence && status !== "abort") self.ErroHistorico = "Não foi possível carregar o histórico. Tente novamente.";
                },
                complete: function () { if (sequence === self._historySequence) self.CarregandoHistorico = false; }
            });
        },
        imagemHistorico: function (arquivo) {
            return "/ConsLutador/ImagemHistorico?id=" + this.Lutador.Id + "&arquivo=" + encodeURIComponent(arquivo);
        },
        formatarDataHistorico: function (valor) {
            var aspNet = /\/Date\((-?\d+)(?:[+-]\d+)?\)\//.exec(valor || "");
            var date = aspNet ? new Date(Number(aspNet[1])) : new Date(valor);
            return isNaN(date.getTime()) ? "Data indisponível" : date.toLocaleString("pt-BR");
        },
        carregarConquistas: function () {
            var self = this;
            if (!self.Lutador || !self.Lutador.Id) {
                return;
            }

            self.CarregandoConquistas = true;
            $.ajax({
                type: "POST",
                url: "/Torre/GetConquistas",
                data: { lutadorId: self.Lutador.Id },
                success: function (data) {
                    if (data && data.sucesso) {
                        self.ConquistasUniverso = data.conquistas || [];

                        if (self.ConquistasUniverso.length && self.TorreConquistaAbertaId === null) {
                            self.TorreConquistaAbertaId = self.ConquistasUniverso[0].IdTorreOrigem;
                        }
                    }
                },
                complete: function () {
                    self.CarregandoConquistas = false;
                }
            });
        },
        alternarTorreConquista: function (conquista) {
            if (!conquista || !conquista.IdTorreOrigem) {
                return;
            }

            this.TorreConquistaAbertaId = this.TorreConquistaAbertaId === conquista.IdTorreOrigem
                ? null
                : conquista.IdTorreOrigem;
        },
        formatarDataConquista: function (conquista) {
            if (!conquista || !conquista.DataPrimeiraConquista) {
                return "--/--/----";
            }

            var valor = conquista.DataPrimeiraConquista;
            var dataAspNet = /\/Date\((-?\d+)(?:[+-]\d+)?\)\//.exec(valor);
            var data = dataAspNet ? new Date(Number(dataAspNet[1])) : new Date(valor);
            return isNaN(data.getTime()) ? "--/--/----" : data.toLocaleDateString("pt-BR");
        },
        usarImagemIndefinida: function (event) {
            if (event && event.target && event.target.src.indexOf("Undefined.png") < 0) {
                event.target.src = "/Imagens/Undefined.png";
            }
        },
        usarImagemGraficoIndefinida: function (event) {
            if (!event || !event.target) {
                return;
            }

            var atual = event.target.getAttribute("href") || "";
            if (atual.indexOf("Undefined.png") < 0) {
                event.target.setAttribute("href", "/Imagens/Undefined.png");
                event.target.setAttributeNS("http://www.w3.org/1999/xlink", "href", "/Imagens/Undefined.png");
            }
        },
        selecionarPontoElo: function (ponto) {
            if (ponto && !ponto.Inicial) {
                this.PontoEloAtivo = ponto;
            }
        },
        formatarVariacaoElo: function (valor) {
            var numero = Number(valor || 0);
            return (numero > 0 ? "+" : "") + numero;
        },
        encurtarNome: function (nome, limite) {
            var texto = nome || "Adversario";
            var tamanho = limite || 14;
            return texto.length > tamanho ? texto.substring(0, tamanho - 1) + "…" : texto;
        },
        getDescricaoPontoElo: function (ponto) {
            if (!ponto || ponto.Inicial) {
                return "Rating Elo inicial: " + this.configuracaoGraficoElo.ratingInicial;
            }

            return (ponto.Vitoria ? "Vitoria" : "Derrota")
                + " contra " + (ponto.AdversarioNome || "adversario")
                + ". Placar " + (ponto.PontosLutador == null ? "?" : ponto.PontosLutador)
                + " a " + (ponto.PontosAdversario == null ? "?" : ponto.PontosAdversario)
                + ". Elo " + ponto.RatingEloAnterior + " para " + ponto.RatingEloAtual
                + " (" + this.formatarVariacaoElo(ponto.VariacaoRatingElo) + "). "
                + (ponto.DataPartida || "Data desconhecida") + ".";
        },
        getNome: function (lutador) {
            return lutador && lutador.Nome ? lutador.Nome : "A definir";
        },
        getImagem: function (lutador) {
            return lutador && lutador.Portrait ? lutador.Portrait : "/Imagens/Undefined.png";
        },
        getNivel: function (lutador) {
            var nivel = lutador && lutador.Nivel ? parseInt(lutador.Nivel, 10) : 1;
            return isNaN(nivel) || nivel < 1 ? 1 : nivel;
        },
        getEstrelasNivel: function (lutador) {
            var estrelas = [];
            var nivel = this.getNivel(lutador);

            for (var estrela = 1; estrela <= nivel; estrela++) {
                estrelas.push(estrela);
            }

            return estrelas;
        },
        getUniversoNome: function (lutador) {
            return lutador && lutador.UniversoNome ? lutador.UniversoNome : "DreamMover";
        },
        getUniversoImagem: function (lutador) {
            if (!lutador) {
                return "/Imagens/Undefined.png";
            }

            if (lutador.Universo) {
                return lutador.Universo;
            }

            return "/Imagens/" + this.getUniversoNome(lutador) + ".png";
        },
        getHeroStyle: function (lutador) {
            return {
                "--fighter-details-universe": "url('" + this.getUniversoImagem(lutador).replace(/'/g, "%27") + "')"
            };
        },
        getExpAtual: function (lutador) {
            return Math.round((lutador && lutador.Exp) || 0);
        },
        getExpMeta: function (lutador) {
            return this.getNivel(lutador) * 1000;
        },
        getExpPercentual: function (lutador) {
            var meta = this.getExpMeta(lutador);

            if (!meta) {
                return 0;
            }

            return Math.max(0, Math.min(100, Math.round((this.getExpAtual(lutador) / meta) * 100)));
        },
        getExpRestante: function (lutador) {
            return Math.max(0, this.getExpMeta(lutador) - this.getExpAtual(lutador));
        },
        getRivaisComHistorico: function () {
            if (!this.Lutador || !this.Lutador.Rivais) {
                return [];
            }

            return this.Lutador.Rivais.filter(function (rival) {
                return (rival.PartidasTotais || 0) > 0;
            });
        },
        getAproveitamentoRival: function (rival) {
            if (!rival || !rival.PartidasTotais) {
                return 0;
            }

            return Math.round(((rival.Vitorias || 0) / rival.PartidasTotais) * 100);
        },
        getResumoRival: function (rival) {
            return (rival.Vitorias || 0) + "V - " + (rival.Derrotas || 0) + "D | " + this.getAproveitamentoRival(rival) + "%";
        },
        getAdversario: function (partida) {
            if (!partida) {
                return "A definir";
            }

            if (partida.IdVencedor == this.Lutador.Id) {
                return partida.Perdedor && partida.Perdedor.Nome ? partida.Perdedor.Nome : "A definir";
            }

            return partida.Vencedor && partida.Vencedor.Nome ? partida.Vencedor.Nome : "A definir";
        },
        getPlacarPartida: function (partida) {
            if (!partida) {
                return "0 x 0";
            }

            if (partida.IdVencedor == this.Lutador.Id) {
                return (partida.PontosVencedor || 0) + " x " + (partida.PontosPerdedor || 0);
            }

            return (partida.PontosPerdedor || 0) + " x " + (partida.PontosVencedor || 0);
        },
        formatarDataPartida: function (partida) {
            var data = partida && (partida.DatePartida || partida.DataPartida);

            if (!data) {
                return "--/--/----";
            }

            var dataObj = new Date(data);

            if (isNaN(dataObj.getTime())) {
                return partida.DataPartida || "--/--/----";
            }

            return dataObj.toLocaleDateString("pt-BR", {
                day: "2-digit",
                month: "2-digit",
                year: "numeric"
            });
        },
        fecharModalDetalhes: function () {
            var seletoresModal = ["#modal-detalhes", "#detalhesModal"];

            if (window.jQuery) {
                seletoresModal.forEach(function (seletor) {
                    var modal = window.jQuery(seletor);

                    if (modal.length) {
                        modal.modal("hide");
                    }
                });

                return;
            }

            seletoresModal.forEach(function (seletor) {
                var modal = document.querySelector(seletor);

                if (!modal) {
                    return;
                }

                modal.classList.remove("show");
                modal.style.display = "none";
                modal.setAttribute("aria-hidden", "true");
            });

            var backdrop = document.querySelector(".modal-backdrop");
            if (backdrop && backdrop.parentNode) {
                backdrop.parentNode.removeChild(backdrop);
            }

            document.body.classList.remove("modal-open");
            document.body.style.paddingRight = "";
        }
    }
});

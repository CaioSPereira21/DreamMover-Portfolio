var appPartida = new Vue({
    el: "#partidaApp",
    mixins: [window.DreamMoverLutadorEditorMixin],
    data: {
        Lutadores: [],
        Cenarios: [],
        CenarioSelecionadoId: "",
        CarregandoCenarios: true,
        LutadorCasa: null,
        LutadorVisitante: null,
        LutadorCasaManual: false,
        LutadorVisitanteManual: false,
        LadoSelecao: "casa",
        BuscaLutador: "",
        UniversoFiltro: "",
        NivelFiltro: "",
        TagFiltro: "",
        OrdenacaoElenco: "nome",
        CarregandoLutadores: true,
        ErroLutadores: "",
        PartidaEmAndamento: false,
        ResultadoRecente: null,
        LutadorCasaBloqueado: false,
        LutadorVisitanteBloqueado: false,
        ModoAutomatico: false,
        ModoDivisoes: true,
        DesafioDivisaoAtual: null,
        PartidasAutomaticas: 0,
        TimerModoAutomatico: null,
        ChanceAdversarioImprovavel: 0.1,
        IntervaloModoAutomaticoMs: 1500,
        TempoLimitePartidaMs: 5 * 60 * 1000,
        MargemRespostaServidorMs: 30 * 1000
    },
    computed: {
        lutadoresFiltrados: function () {
            var self = this;
            var busca = this.normalizarTexto(this.BuscaLutador);
            var universo = this.normalizarTexto(this.UniversoFiltro);
            var tagFiltro = this.normalizarTexto(this.TagFiltro);
            var nivel = this.NivelFiltro === "" ? null : Number(this.NivelFiltro);

            var resultado = this.Lutadores.filter(function (lutador) {
                var tags = (lutador.Tags || []).map(function (tag) {
                    return tag.Nome || "";
                });
                var texto = [lutador.Nome, lutador.UniversoNome, lutador.Autor, tags.join(" ")].join(" ");
                var correspondeBusca = !busca || self.normalizarTexto(texto).indexOf(busca) !== -1;
                var correspondeUniverso = !universo || self.normalizarTexto(lutador.UniversoNome) === universo;
                var correspondeNivel = nivel === null || Number(lutador.Nivel || 1) === nivel;
                var correspondeTag = !tagFiltro || tags.some(function (tag) {
                    return self.normalizarTexto(tag) === tagFiltro;
                });

                return correspondeBusca && correspondeUniverso && correspondeNivel && correspondeTag;
            });

            return resultado.slice().sort(function (a, b) {
                var nomeA = self.normalizarTexto(a.Nome);
                var nomeB = self.normalizarTexto(b.Nome);
                var desempate = nomeA.localeCompare(nomeB);

                if (self.OrdenacaoElenco === "elo-desc") {
                    return self.getRating(b) - self.getRating(a) || desempate;
                }

                if (self.OrdenacaoElenco === "elo-asc") {
                    return self.getRating(a) - self.getRating(b) || desempate;
                }

                if (self.OrdenacaoElenco === "nivel-desc") {
                    return Number(b.Nivel || 1) - Number(a.Nivel || 1) ||
                        self.getRating(b) - self.getRating(a) || desempate;
                }

                return desempate;
            });
        },
        universosDisponiveis: function () {
            var self = this;
            var vistos = {};

            return this.Lutadores.map(function (lutador) {
                return String(lutador.UniversoNome || "").trim();
            }).filter(function (universo) {
                var chave = self.normalizarTexto(universo);

                if (!chave || vistos[chave]) {
                    return false;
                }

                vistos[chave] = true;
                return true;
            }).sort(function (a, b) {
                return self.normalizarTexto(a).localeCompare(self.normalizarTexto(b));
            });
        },
        niveisDisponiveis: function () {
            return this.Lutadores.map(function (lutador) {
                return Number(lutador.Nivel || 1);
            }).filter(function (nivel, indice, niveis) {
                return niveis.indexOf(nivel) === indice;
            }).sort(function (a, b) {
                return a - b;
            });
        },
        tagsDisponiveis: function () {
            var self = this;
            var vistos = {};
            var tags = [];

            this.Lutadores.forEach(function (lutador) {
                (lutador.Tags || []).forEach(function (tag) {
                    var nome = String(tag.Nome || "").trim();
                    var chave = self.normalizarTexto(nome);

                    if (chave && !vistos[chave]) {
                        vistos[chave] = true;
                        tags.push(nome);
                    }
                });
            });

            return tags.sort(function (a, b) {
                return self.normalizarTexto(a).localeCompare(self.normalizarTexto(b));
            });
        },
        temFiltrosAtivos: function () {
            return !!(this.BuscaLutador ||
                this.UniversoFiltro ||
                this.NivelFiltro !== "" ||
                this.TagFiltro ||
                this.OrdenacaoElenco !== "nome");
        },
        cenarioSelecionado: function () {
            var id = Number(this.CenarioSelecionadoId);
            return this.Cenarios.find(function (cenario) { return cenario.Id === id; }) || null;
        },
        totalSelecionados: function () {
            return (this.LutadorCasa ? 1 : 0) + (this.LutadorVisitante ? 1 : 0);
        },
        confrontoDivisaoValido: function () {
            if (!this.ModoDivisoes || !this.LutadorCasa || !this.LutadorVisitante) {
                return true;
            }

            if (this.DesafioDivisaoAtual) {
                var ids = [this.LutadorCasa.Id, this.LutadorVisitante.Id];
                return ids.indexOf(this.DesafioDivisaoAtual.DesafianteId) !== -1 &&
                    ids.indexOf(this.DesafioDivisaoAtual.ChefeId) !== -1;
            }

            return Number(this.LutadorCasa.Divisao) === Number(this.LutadorVisitante.Divisao) &&
                !this.LutadorCasa.DesafioDivisaoPendente &&
                !this.LutadorVisitante.DesafioDivisaoPendente;
        },
        podeIniciarPartida: function () {
            return !!(this.LutadorCasa &&
                this.LutadorVisitante &&
                this.LutadorCasa.Id !== this.LutadorVisitante.Id &&
                this.confrontoDivisaoValido);
        },
        diferencaElo: function () {
            if (!this.podeIniciarPartida) {
                return null;
            }

            return Math.abs(this.getRating(this.LutadorCasa) - this.getRating(this.LutadorVisitante));
        },
        statusSelecao: function () {
            if (this.PartidaEmAndamento) {
                return this.ModoAutomatico ? "Modo automatico em andamento" : "Batalha em andamento";
            }

            if (this.ModoAutomatico) {
                return "Modo automatico ativo";
            }

            if (this.podeIniciarPartida) {
                return "Confronto pronto";
            }

            if (this.totalSelecionados === 2 && this.ModoDivisoes && !this.confrontoDivisaoValido) {
                return "Divisões incompatíveis";
            }

            if (this.totalSelecionados === 1) {
                return "Falta um lutador";
            }

            return "Aguardando seleção";
        },
        orientacaoSelecao: function () {
            if (this.PartidaEmAndamento) {
                return this.ModoAutomatico
                    ? "A sequencia continua ao final da batalha"
                    : "Aguarde o resultado da batalha";
            }

            if (this.ModoAutomatico) {
                return "Preparando o proximo confronto";
            }

            if (this.podeIniciarPartida) {
                return "Tudo pronto para começar";
            }

            if (this.totalSelecionados === 2 && this.ModoDivisoes && !this.confrontoDivisaoValido) {
                return "Escolha personagens da mesma divisão ou aguarde uma luta decisiva";
            }

            if (!this.LutadorCasa) {
                return "Escolha o lutador do P1";
            }

            return "Agora escolha o rival do P2";
        },
        descricaoEquilibrio: function () {
            if (this.diferencaElo === null) {
                return "";
            }

            if (this.diferencaElo <= 75) {
                return "Duelo muito equilibrado";
            }

            if (this.diferencaElo <= 150) {
                return "Duelo competitivo";
            }

            return "Há uma vantagem relevante de Elo";
        },
        dicaElenco: function () {
            if (this.ModoAutomatico) {
                return "Modo automatico ativo. Use os cadeados para manter P1 ou P2.";
            }

            if (this.LadoSelecao === "casa") {
                return "Selecionando o lado esquerdo (P1).";
            }

            if (this.LadoSelecao === "visitante") {
                return "Selecionando o lado direito (P2).";
            }

            if (this.podeIniciarPartida) {
                return "Confronto completo. Use “Trocar” para alterar um lado.";
            }

            return "Selecione um lutador para continuar.";
        }
    },
    methods: {
        getLutadores: function () {
            var self = this;

            self.CarregandoLutadores = true;
            self.ErroLutadores = "";

            $.ajax({
                type: "POST",
                url: "/ConsLutador/GetLutadores",
                success: function (data) {
                    if (!data || data.sucesso === false || !Array.isArray(data.lutadores)) {
                        self.ErroLutadores = "A arena retornou uma lista inválida.";
                        return;
                    }

                    self.Lutadores = data.lutadores;
                    if (self.ModoDivisoes) {
                        self.carregarDesafioDivisaoPendente();
                    }
                },
                error: function () {
                    self.ErroLutadores = "Verifique a conexão com o servidor e tente novamente.";
                },
                complete: function () {
                    self.CarregandoLutadores = false;
                }
            });
        },
        getCenarios: function () {
            var self = this;
            self.CarregandoCenarios = true;
            $.ajax({
                type: "POST",
                url: "/Cenario/Listar",
                success: function (data) {
                    if (!data || !data.sucesso || !Array.isArray(data.cenarios)) {
                        if (window.toastr) toastr.warning("Não foi possível carregar os cenários. A seleção continuará automática.");
                        return;
                    }

                    self.Cenarios = data.cenarios.filter(function (cenario) {
                        return cenario.Ativo && !cenario.Excluido && cenario.ArquivoEncontrado;
                    }).sort(function (a, b) {
                        return String(a.Nome || "").localeCompare(String(b.Nome || ""), "pt-BR");
                    });
                },
                error: function () {
                    if (window.toastr) toastr.warning("Não foi possível carregar os cenários. A seleção continuará automática.");
                },
                complete: function () { self.CarregandoCenarios = false; }
            });
        },
        normalizarTexto: function (texto) {
            var valor = String(texto || "").toLowerCase().trim();

            if (valor.normalize) {
                valor = valor.normalize("NFD").replace(/[\u0300-\u036f]/g, "");
            }

            return valor;
        },
        limparFiltros: function () {
            this.BuscaLutador = "";
            this.UniversoFiltro = "";
            this.NivelFiltro = "";
            this.TagFiltro = "";
            this.OrdenacaoElenco = "nome";
        },
        getRating: function (lutador) {
            return Number(lutador && lutador.RatingElo || 1000);
        },
        alternarModoDivisoes: function () {
            if (this.PartidaEmAndamento || this.ModoAutomatico) {
                return;
            }

            this.ModoDivisoes = !this.ModoDivisoes;
            this.DesafioDivisaoAtual = null;
            this.limparSelecao();

            if (this.ModoDivisoes) {
                this.carregarDesafioDivisaoPendente();
                if (window.toastr) toastr.info("Modo Divisões ativado. Apenas confrontos da mesma divisão contarão sequência.");
            } else if (window.toastr) {
                toastr.info("Modo exibição ativado. Estas partidas não alteram divisões ou sequências.");
            }
        },
        carregarDesafioDivisaoPendente: function () {
            var self = this;
            if (!self.ModoDivisoes || !self.Lutadores.length) return;

            $.ajax({
                type: "POST",
                url: "/Partida/GetDesafioDivisaoPendente",
                success: function (data) {
                    if (data && data.sucesso && data.desafio) {
                        self.prepararDesafioDivisao(data.desafio);
                    }
                }
            });
        },
        prepararDesafioDivisao: function (desafio) {
            if (!desafio) {
                this.DesafioDivisaoAtual = null;
                return false;
            }

            var desafiante = this.Lutadores.find(function (item) { return item.Id === desafio.DesafianteId; });
            var chefe = this.Lutadores.find(function (item) { return item.Id === desafio.ChefeId; });
            if (!desafiante || !chefe) return false;

            this.DesafioDivisaoAtual = desafio;
            this.LutadorCasa = desafiante;
            this.LutadorVisitante = chefe;
            this.LutadorCasaManual = false;
            this.LutadorVisitanteManual = false;
            this.LutadorCasaBloqueado = false;
            this.LutadorVisitanteBloqueado = false;
            this.LadoSelecao = "";
            return true;
        },
        ehLutadorCasa: function (lutador) {
            return !!(lutador && this.LutadorCasa && lutador.Id === this.LutadorCasa.Id);
        },
        ehLutadorVisitante: function (lutador) {
            return !!(lutador && this.LutadorVisitante && lutador.Id === this.LutadorVisitante.Id);
        },
        getRotuloSelecao: function (lutador) {
            if (this.ehLutadorCasa(lutador)) {
                return lutador.Nome + ", selecionado como P1";
            }

            if (this.ehLutadorVisitante(lutador)) {
                return lutador.Nome + ", selecionado como P2";
            }

            return "Selecionar " + lutador.Nome;
        },
        selecionarLutador: function (lutador) {
            if (!lutador || this.PartidaEmAndamento || this.ModoAutomatico) {
                return;
            }

            var lado = this.LadoSelecao;

            if (!lado) {
                if (!this.LutadorCasa) {
                    lado = "casa";
                } else if (!this.LutadorVisitante) {
                    lado = "visitante";
                } else if (this.ehLutadorCasa(lutador)) {
                    lado = "casa";
                } else if (this.ehLutadorVisitante(lutador)) {
                    lado = "visitante";
                } else {
                    if (window.toastr) {
                        toastr.info("Use o botão Trocar no lado que deseja alterar.");
                    }
                    return;
                }
            }

            if ((lado === "casa" && this.LutadorCasaBloqueado) ||
                (lado === "visitante" && this.LutadorVisitanteBloqueado)) {
                if (window.toastr) {
                    toastr.info("Desbloqueie esse lado antes de trocar o lutador.");
                }
                return;
            }

            if ((lado === "casa" && this.ehLutadorCasa(lutador)) ||
                (lado === "visitante" && this.ehLutadorVisitante(lutador))) {
                return;
            }

            this.DesafioDivisaoAtual = null;

            if (lado === "casa") {
                if (this.ehLutadorVisitante(lutador)) {
                    this.alertarLutadorDuplicado();
                    return;
                }

                this.LutadorCasa = lutador;
                this.LutadorCasaManual = true;
                this.LadoSelecao = this.LutadorVisitante ? "" : "visitante";
            } else {
                if (this.ehLutadorCasa(lutador)) {
                    this.alertarLutadorDuplicado();
                    return;
                }

                this.LutadorVisitante = lutador;
                this.LutadorVisitanteManual = true;
                this.LadoSelecao = this.LutadorCasa ? "" : "casa";
            }

            this.ResultadoRecente = null;

            var self = this;
            this.$nextTick(function () {
                if (!self.podeIniciarPartida || !window.matchMedia("(max-width: 767px)").matches) {
                    return;
                }

                var iniciar = document.querySelector(".battle-start");
                if (!iniciar) {
                    return;
                }

                var reduzirMovimento = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
                iniciar.scrollIntoView({
                    behavior: reduzirMovimento ? "auto" : "smooth",
                    block: "center"
                });
            });
        },
        alertarLutadorDuplicado: function () {
            if (window.toastr) {
                toastr.warning("Escolha um adversário diferente para cada lado.");
            }
        },
        ativarLado: function (lado) {
            if (this.PartidaEmAndamento || this.ModoAutomatico ||
                (lado !== "casa" && lado !== "visitante") ||
                (lado === "casa" && this.LutadorCasaBloqueado) ||
                (lado === "visitante" && this.LutadorVisitanteBloqueado)) {
                return;
            }

            this.LadoSelecao = lado;

            this.$nextTick(function () {
                var busca = document.querySelector(".battle-search input");
                if (busca) {
                    var reduzirMovimento = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
                    busca.scrollIntoView({
                        behavior: reduzirMovimento ? "auto" : "smooth",
                        block: "center"
                    });
                    busca.focus({ preventScroll: true });
                }
            });
        },
        removerLutador: function (lado) {
            if (this.PartidaEmAndamento || this.ModoAutomatico ||
                (lado === "casa" && this.LutadorCasaBloqueado) ||
                (lado === "visitante" && this.LutadorVisitanteBloqueado)) {
                return;
            }

            this.DesafioDivisaoAtual = null;

            if (lado === "casa") {
                this.LutadorCasa = null;
                this.LutadorCasaManual = false;
            } else {
                this.LutadorVisitante = null;
                this.LutadorVisitanteManual = false;
            }

            this.LadoSelecao = lado;
            this.ResultadoRecente = null;
        },
        limparSelecao: function () {
            if (this.PartidaEmAndamento || this.ModoAutomatico) {
                return;
            }

            this.DesafioDivisaoAtual = null;

            if (!this.LutadorCasaBloqueado) {
                this.LutadorCasa = null;
                this.LutadorCasaManual = false;
            }
            if (!this.LutadorVisitanteBloqueado) {
                this.LutadorVisitante = null;
                this.LutadorVisitanteManual = false;
            }

            this.LadoSelecao = !this.LutadorCasa && !this.LutadorCasaBloqueado
                ? "casa"
                : (!this.LutadorVisitante && !this.LutadorVisitanteBloqueado ? "visitante" : "");
            this.ResultadoRecente = null;
        },
        alternarBloqueio: function (lado) {
            if (lado === "casa" && this.LutadorCasa) {
                this.LutadorCasaBloqueado = !this.LutadorCasaBloqueado;
                if (this.LutadorCasaBloqueado && this.LadoSelecao === "casa") {
                    this.LadoSelecao = "";
                }
            } else if (lado === "visitante" && this.LutadorVisitante) {
                this.LutadorVisitanteBloqueado = !this.LutadorVisitanteBloqueado;
                if (this.LutadorVisitanteBloqueado && this.LadoSelecao === "visitante") {
                    this.LadoSelecao = "";
                }
            }
        },
        encontrarAdversarioMaisProximo: function (referencia) {
            if (!referencia) {
                return null;
            }

            var self = this;
            var ratingReferencia = self.getRating(referencia);
            var candidatos = self.Lutadores
                .filter(function (lutador) {
                    return lutador.Id !== referencia.Id &&
                        (!self.ModoDivisoes ||
                            (Number(lutador.Divisao) === Number(referencia.Divisao) &&
                                !lutador.DesafioDivisaoPendente &&
                                !referencia.DesafioDivisaoPendente));
                })
                .map(function (lutador) {
                    return {
                        lutador: lutador,
                        distancia: Math.abs(self.getRating(lutador) - ratingReferencia)
                    };
                })
                .sort(function (a, b) {
                    return a.distancia - b.distancia || a.lutador.Nome.localeCompare(b.lutador.Nome);
                });

            if (!candidatos.length) {
                return null;
            }

            if (candidatos.length >= 3 && Math.random() < this.ChanceAdversarioImprovavel) {
                var indiceFaixaDistante = Math.floor(candidatos.length * 0.75);
                var distanciaMinimaImprovavel = Math.max(200, candidatos[indiceFaixaDistante].distancia);
                var adversariosImprovaveis = candidatos.filter(function (item) {
                    return item.distancia >= distanciaMinimaImprovavel;
                });

                if (adversariosImprovaveis.length) {
                    return adversariosImprovaveis[Math.floor(Math.random() * adversariosImprovaveis.length)].lutador;
                }
            }

            var menorDistancia = candidatos[0].distancia;
            var maisProximos = candidatos.filter(function (item) {
                return item.distancia === menorDistancia;
            });

            return maisProximos[Math.floor(Math.random() * maisProximos.length)].lutador;
        },
        sortearLutadorComPrioridadeDeSequencia: function (lutadores) {
            if (!lutadores || !lutadores.length) {
                return null;
            }

            var pesos = lutadores.map(function (lutador) {
                var sequencia = Math.abs(Number(lutador.SequenciaDivisao) || 0);
                return 1 + Math.min(sequencia, 10);
            });
            var pesoTotal = pesos.reduce(function (total, peso) {
                return total + peso;
            }, 0);
            var sorteio = Math.random() * pesoTotal;

            for (var indice = 0; indice < lutadores.length; indice++) {
                if (sorteio < pesos[indice]) {
                    return lutadores[indice];
                }

                sorteio -= pesos[indice];
            }

            return lutadores[lutadores.length - 1];
        },
        sortearConfrontoPorElo: function (forcarNovaDupla) {
            if (this.Lutadores.length < 2) {
                if (window.toastr) {
                    toastr.warning("Cadastre pelo menos dois lutadores para montar um confronto.");
                }
                return false;
            }

            if (this.ModoDivisoes && this.DesafioDivisaoAtual) {
                return this.prepararDesafioDivisao(this.DesafioDivisaoAtual);
            }

            if (this.LutadorCasaBloqueado && this.LutadorVisitanteBloqueado) {
                return this.podeIniciarPartida;
            }

            if (this.LutadorCasaBloqueado) {
                this.LutadorVisitante = this.encontrarAdversarioMaisProximo(this.LutadorCasa);
                this.LutadorVisitanteManual = false;
            } else if (this.LutadorVisitanteBloqueado) {
                this.LutadorCasa = this.encontrarAdversarioMaisProximo(this.LutadorVisitante);
                this.LutadorCasaManual = false;
            } else if (!forcarNovaDupla && this.LutadorCasaManual && this.LutadorCasa) {
                this.LutadorVisitante = this.encontrarAdversarioMaisProximo(this.LutadorCasa);
                this.LutadorVisitanteManual = false;
            } else if (!forcarNovaDupla && this.LutadorVisitanteManual && this.LutadorVisitante) {
                this.LutadorCasa = this.encontrarAdversarioMaisProximo(this.LutadorVisitante);
                this.LutadorCasaManual = false;
            } else {
                var self = this;
                var bases = this.Lutadores.filter(function (lutador) {
                    return !self.ModoDivisoes || (!lutador.DesafioDivisaoPendente && self.Lutadores.some(function (outro) {
                        return outro.Id !== lutador.Id &&
                            !outro.DesafioDivisaoPendente &&
                            Number(outro.Divisao) === Number(lutador.Divisao);
                    }));
                });
                if (!bases.length) return false;
                this.LutadorCasa = this.ModoAutomatico
                    ? this.sortearLutadorComPrioridadeDeSequencia(bases)
                    : bases[Math.floor(Math.random() * bases.length)];
                this.LutadorVisitante = this.encontrarAdversarioMaisProximo(this.LutadorCasa);
                this.LutadorCasaManual = false;
                this.LutadorVisitanteManual = false;
            }

            this.LadoSelecao = "";
            this.ResultadoRecente = null;
            return this.podeIniciarPartida;
        },
        selecaoEquilibrada: function () {
            if (this.PartidaEmAndamento || this.ModoAutomatico) {
                return;
            }

            if (this.LutadorCasaBloqueado && this.LutadorVisitanteBloqueado) {
                if (window.toastr) {
                    toastr.info("P1 e P2 estao bloqueados. Desbloqueie um lado para sortear novamente.");
                }
                return;
            }

            if (!this.sortearConfrontoPorElo(false)) {
                return;
            }

            if (window.toastr) {
                toastr.success("Confronto sorteado com base no Elo atual.");
            }
        },
        alternarModoAutomatico: function () {
            if (this.ModoAutomatico) {
                this.desativarModoAutomatico(true);
                return;
            }

            if (this.Lutadores.length < 2) {
                if (window.toastr) {
                    toastr.warning("Cadastre pelo menos dois lutadores para ativar o modo automatico.");
                }
                return;
            }

            this.ModoAutomatico = true;
            this.PartidasAutomaticas = 0;

            if (!this.podeIniciarPartida && !this.sortearConfrontoPorElo(true)) {
                this.desativarModoAutomatico(false);
                return;
            }

            if (window.toastr) {
                toastr.info("Modo automatico ativado. O sorteio por Elo priorizara lutadores em sequencia.");
            }

            this.iniciarPartida();
        },
        desativarModoAutomatico: function (mostrarMensagem) {
            if (this.TimerModoAutomatico) {
                window.clearTimeout(this.TimerModoAutomatico);
                this.TimerModoAutomatico = null;
            }

            var estavaAtivo = this.ModoAutomatico;
            this.ModoAutomatico = false;

            if (mostrarMensagem && estavaAtivo && window.toastr) {
                toastr.info(this.PartidaEmAndamento
                    ? "Modo automatico desativado. A partida atual sera concluida."
                    : "Modo automatico desativado.");
            }
        },
        agendarProximaPartidaAutomatica: function () {
            var self = this;

            if (!self.ModoAutomatico) {
                return;
            }

            if (self.TimerModoAutomatico) {
                window.clearTimeout(self.TimerModoAutomatico);
            }

            self.TimerModoAutomatico = window.setTimeout(function () {
                self.TimerModoAutomatico = null;

                if (!self.ModoAutomatico || self.PartidaEmAndamento) {
                    return;
                }

                if (!self.sortearConfrontoPorElo(true)) {
                    self.desativarModoAutomatico(false);
                    if (window.toastr) {
                        toastr.error("O modo automatico foi interrompido porque nao foi possivel montar o proximo confronto.");
                    }
                    return;
                }

                self.iniciarPartida();
            }, self.IntervaloModoAutomaticoMs);
        },
        aplicarProgressao: function (progressao) {
            var self = this;

            if (!progressao) {
                return;
            }

            [progressao.Casa, progressao.Visitante].forEach(function (resultado) {
                if (!resultado) {
                    return;
                }

                var lutador = self.Lutadores.find(function (item) {
                    return item.Id === resultado.Id;
                });

                if (!lutador) {
                    return;
                }

                self.$set(lutador, "Exp", resultado.ExperienciaAtual);
                self.$set(lutador, "Nivel", resultado.NivelAtual);
                self.$set(lutador, "RatingElo", resultado.RatingEloAtual);
            });
        },
        aplicarResultadoDivisao: function (divisao) {
            var self = this;
            if (!divisao) return;

            (divisao.Lutadores || []).forEach(function (estado) {
                var lutador = self.Lutadores.find(function (item) { return item.Id === estado.Id; });
                if (!lutador) return;
                self.$set(lutador, "Divisao", estado.Divisao);
                self.$set(lutador, "DivisaoNome", estado.DivisaoNome);
                self.$set(lutador, "SequenciaDivisao", estado.Sequencia);
                self.$set(lutador, "PartidasDivisao", estado.PartidasDivisao);
                self.$set(lutador, "DesafioDivisaoPendente", estado.DesafioPendente);
                self.$set(lutador, "TipoDesafioDivisao", estado.TipoDesafio);
                self.$set(lutador, "EmColocacao", estado.EmColocacao);
                self.$set(lutador, "PartidasColocacao", estado.PartidasColocacao);
                self.$set(lutador, "TotalPartidasColocacao", estado.TotalPartidasColocacao);
            });

            self.DesafioDivisaoAtual = null;
            if (divisao.ProximoDesafio) {
                self.prepararDesafioDivisao(divisao.ProximoDesafio);
            }

            if (window.toastr && divisao.Mensagem) {
                var titulo = divisao.Resultado === "PROMOVIDO"
                    ? "Promoção confirmada"
                    : (divisao.Resultado === "REBAIXADO"
                        ? "Rebaixamento confirmado"
                        : (divisao.Resultado === "COLOCADO"
                            ? "Colocação concluída"
                            : (divisao.Resultado === "EM_COLOCACAO" ? "Colocação atualizada" : "Divisão mantida")));
                toastr.info(divisao.Mensagem, titulo, { timeOut: 9000 });
            }
        },
        getResumoProgressao: function (resultado) {
            if (!resultado) {
                return "";
            }

            var sinalElo = resultado.VariacaoRatingElo >= 0 ? "+" : "";
            var nivel = resultado.SubiuDeNivel ? " | Nível " + resultado.NivelAtual + "!" : "";
            return resultado.Nome + ": +" + resultado.ExperienciaGanha + " XP | Elo "
                + sinalElo + resultado.VariacaoRatingElo + nivel;
        },
        iniciarPartida: function () {
            var self = this;

            if (self.PartidaEmAndamento) {
                return;
            }

            if (!self.podeIniciarPartida) {
                if (window.toastr) {
                    toastr.warning("Selecione dois lutadores diferentes antes de iniciar.");
                }
                return;
            }

            var lutadorCasa = self.LutadorCasa;
            var lutadorVisitante = self.LutadorVisitante;
            self.PartidaEmAndamento = true;
            self.ResultadoRecente = null;

            $.ajax({
                type: "POST",
                url: "/Partida/IniciarPartida",
                data: {
                    lutadorCasa: lutadorCasa,
                    lutadorVisitante: lutadorVisitante,
                    cenarioId: self.CenarioSelecionadoId === "" ? null : Number(self.CenarioSelecionadoId),
                    partidaDivisao: self.ModoDivisoes,
                    desafianteDivisaoId: self.DesafioDivisaoAtual ? self.DesafioDivisaoAtual.DesafianteId : null
                },
                timeout: self.TempoLimitePartidaMs + self.MargemRespostaServidorMs,
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) {
                            var mensagem = data && data.mensagem
                                ? data.mensagem
                                : "A partida não gerou um resultado válido.";

                            if (data && data.codigoErro === "PARTIDA_TIMEOUT") {
                                toastr.error(mensagem, "Luta encerrada por segurança");
                            } else {
                                toastr.warning(mensagem);
                            }
                        }
                        return;
                    }

                    self.ResultadoRecente = {
                        PontosCasa: data.pontosLutadorCasa,
                        PontosVisitante: data.pontosLutadorVisitante,
                        VencedorId: data.vencedorId
                    };
                    self.aplicarProgressao(data.progressao);
                    self.aplicarResultadoDivisao(data.divisao);
                    if (self.ModoAutomatico) {
                        self.PartidasAutomaticas += 1;
                    }

                    if (window.toastr && data.progressao) {
                        toastr.success(
                            self.getResumoProgressao(data.progressao.Casa)
                            + "<br>"
                            + self.getResumoProgressao(data.progressao.Visitante),
                            "Progressão atualizada",
                            { timeOut: 7000, escapeHtml: false }
                        );
                    }
                },
                error: function (xhr, status) {
                    if (window.toastr) {
                        toastr.error(status === "timeout"
                            ? "A partida excedeu o tempo de segurança e a tela foi liberada."
                            : "Não foi possível registrar a partida.");
                    }
                },
                complete: function () {
                    self.PartidaEmAndamento = false;

                    if (self.ModoAutomatico) {
                        self.agendarProximaPartidaAutomatica();
                    }
                }
            });
        },
        getNomeVencedorResultado: function () {
            if (!this.ResultadoRecente) {
                return "";
            }

            if (this.LutadorCasa && this.ResultadoRecente.VencedorId === this.LutadorCasa.Id) {
                return this.LutadorCasa.Nome;
            }

            if (this.LutadorVisitante && this.ResultadoRecente.VencedorId === this.LutadorVisitante.Id) {
                return this.LutadorVisitante.Nome;
            }

            return "O vencedor";
        },
        usarImagemPadrao: function (event) {
            var imagem = event && event.target;

            if (!imagem || imagem.getAttribute("data-dm-fallback") === "true") {
                return;
            }

            imagem.setAttribute("data-dm-fallback", "true");
            imagem.src = "/Images/Icon-v2.png";
        },
        abrirModal: async function (id) {
            if (!id) {
                return;
            }

            var modalContent = document.getElementById("conteudoModal");

            try {
                modalContent.setAttribute("aria-busy", "true");
                modalContent.innerHTML = '<div class="fighter-details-loading"><i class="fa fa-circle-o-notch fa-spin" aria-hidden="true"></i> Carregando perfil...</div>';
                $("#detalhesModal").modal("show");

                var response = await fetch("/Partida/DetalhesLutador?id=" + encodeURIComponent(id));
                if (!response.ok) {
                    throw new Error("Falha ao carregar o perfil.");
                }

                modalContent.innerHTML = await response.text();
                modalContent.setAttribute("aria-busy", "false");

                var scriptAnterior = document.getElementById("detalhesLutadorScriptModal");
                if (scriptAnterior) {
                    scriptAnterior.parentNode.removeChild(scriptAnterior);
                }

                var script = document.createElement("script");
                script.id = "detalhesLutadorScriptModal";
                script.src = "/Content/js/Views/DetalhesLutador/Index.js?modal=" + Date.now();
                document.body.appendChild(script);
            } catch (error) {
                modalContent.setAttribute("aria-busy", "false");
                modalContent.innerHTML = '<div class="fighter-details-loading">Não foi possível carregar o perfil.</div>';
                if (window.toastr) {
                    toastr.error("Não foi possível carregar os detalhes do lutador.");
                }
                console.error("Erro ao carregar detalhes do lutador:", error);
            }
        }
    },
    mounted: function () {
        this.getLutadores();
        this.getCenarios();
    },
    beforeDestroy: function () {
        this.desativarModoAutomatico(false);
    }
});

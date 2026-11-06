(function () {
    "use strict";

    var STORAGE_KEY = "dreammover.torreAtualId";

    new Vue({
        el: "#torreApp",
        data: {
            Lutadores: [],
            LutadorId: "",
            BuscaLutador: "",
            FiltroUniversoLutador: "",
            FiltroNivelLutador: "",
            FiltroTagLutador: "",
            OrdenacaoLutadores: "nome",
            Universos: [],
            UniversoSelecionado: null,
            Torre: null,
            CarregandoLutadores: false,
            CarregandoUniversos: false,
            CarregandoDetalhes: false,
            CriandoTorre: false,
            ExecutandoAndar: false
        },
        computed: {
            LutadorSelecionado: function () {
                var id = Number(this.LutadorId);
                return this.Lutadores.find(function (lutador) {
                    return lutador.Id === id;
                }) || null;
            },
            LutadoresFiltrados: function () {
                var self = this;
                var busca = this.normalizarTexto(this.BuscaLutador);
                var universo = this.normalizarTexto(this.FiltroUniversoLutador);
                var tagFiltro = this.normalizarTexto(this.FiltroTagLutador);
                var nivel = this.FiltroNivelLutador === "" ? null : Number(this.FiltroNivelLutador);

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

                    if (self.OrdenacaoLutadores === "elo-desc") {
                        return Number(b.RatingElo || 1000) - Number(a.RatingElo || 1000) || desempate;
                    }

                    if (self.OrdenacaoLutadores === "elo-asc") {
                        return Number(a.RatingElo || 1000) - Number(b.RatingElo || 1000) || desempate;
                    }

                    if (self.OrdenacaoLutadores === "nivel-desc") {
                        return Number(b.Nivel || 1) - Number(a.Nivel || 1)
                            || Number(b.RatingElo || 1000) - Number(a.RatingElo || 1000)
                            || desempate;
                    }

                    return desempate;
                });
            },
            UniversosDosLutadores: function () {
                return this.valoresUnicosOrdenados(this.Lutadores.map(function (lutador) {
                    return lutador.UniversoNome;
                }));
            },
            NiveisDosLutadores: function () {
                return this.Lutadores.map(function (lutador) {
                    return Number(lutador.Nivel || 1);
                }).filter(function (nivel, indice, niveis) {
                    return niveis.indexOf(nivel) === indice;
                }).sort(function (a, b) {
                    return a - b;
                });
            },
            TagsDosLutadores: function () {
                var tags = [];
                this.Lutadores.forEach(function (lutador) {
                    (lutador.Tags || []).forEach(function (tag) {
                        tags.push(tag.Nome);
                    });
                });
                return this.valoresUnicosOrdenados(tags);
            },
            TemFiltrosLutadoresAtivos: function () {
                return !!(this.BuscaLutador
                    || this.FiltroUniversoLutador
                    || this.FiltroNivelLutador !== ""
                    || this.FiltroTagLutador
                    || this.OrdenacaoLutadores !== "nome");
            },
            PodeCriarTorre: function () {
                return !!this.LutadorSelecionado
                    && !!this.UniversoSelecionado
                    && !this.CriandoTorre
                    && !this.ExecutandoAndar;
            },
            AndaresVencidos: function () {
                if (!this.Torre || !this.Torre.Andares) {
                    return 0;
                }

                return this.Torre.Andares.filter(function (andar) {
                    return andar.Status === "Vencido";
                }).length;
            },
            ProgressoTorre: function () {
                if (!this.Torre || !this.Torre.TotalAndares) {
                    return 0;
                }

                return Math.round(this.AndaresVencidos * 100 / this.Torre.TotalAndares);
            },
            ProximoAndar: function () {
                if (!this.Torre || this.Torre.Status !== "EmAndamento") {
                    return null;
                }

                return this.Torre.Andares.find(function (andar) {
                    return andar.Status === "Pendente";
                }) || null;
            },
            textoStatusTorre: function () {
                if (!this.Torre) {
                    return "";
                }

                if (this.Torre.Status === "Concluida") {
                    return "Torre conclu\u00edda e ins\u00edgnia registrada.";
                }

                if (this.Torre.Status === "Fracassada") {
                    return "O desafiante foi derrotado. A ins\u00edgnia anterior, se houver, continua v\u00e1lida.";
                }

                if (this.Torre.Status === "Abandonada") {
                    return "Esta tentativa foi substitu\u00edda por uma nova torre.";
                }

                return "A sequ\u00eancia e os valores de Elo abaixo est\u00e3o congelados para esta tentativa.";
            }
        },
        methods: {
            carregarLutadores: function () {
                var self = this;
                self.CarregandoLutadores = true;

                $.ajax({
                    type: "POST",
                    url: "/Torre/GetLutadores",
                    success: function (data) {
                        if (!data || !data.sucesso) {
                            self.notificarErro(data && data.mensagem);
                            return;
                        }

                        self.Lutadores = data.lutadores || [];
                        self.restaurarTorre();
                    },
                    error: function () {
                        self.notificarErro("N\u00e3o foi poss\u00edvel carregar os lutadores.");
                    },
                    complete: function () {
                        self.CarregandoLutadores = false;
                    }
                });
            },
            normalizarTexto: function (texto) {
                var valor = String(texto || "").toLowerCase().trim();

                if (valor.normalize) {
                    valor = valor.normalize("NFD").replace(/[\u0300-\u036f]/g, "");
                }

                return valor;
            },
            valoresUnicosOrdenados: function (valores) {
                var self = this;
                var vistos = {};

                return valores.map(function (valor) {
                    return String(valor || "").trim();
                }).filter(function (valor) {
                    var chave = self.normalizarTexto(valor);
                    if (!chave || vistos[chave]) {
                        return false;
                    }

                    vistos[chave] = true;
                    return true;
                }).sort(function (a, b) {
                    return self.normalizarTexto(a).localeCompare(self.normalizarTexto(b));
                });
            },
            limparFiltrosLutadores: function () {
                this.BuscaLutador = "";
                this.FiltroUniversoLutador = "";
                this.FiltroNivelLutador = "";
                this.FiltroTagLutador = "";
                this.OrdenacaoLutadores = "nome";
            },
            selecionarLutador: function (lutador) {
                if (!lutador || this.ExecutandoAndar) {
                    return;
                }

                this.LutadorId = lutador.Id;
                this.UniversoSelecionado = null;
                this.Universos = [];

                if (!this.LutadorSelecionado) {
                    return;
                }

                this.carregarUniversos();
            },
            abrirModal: async function (id) {
                if (!id || this.CarregandoDetalhes) {
                    return;
                }

                var modalContent = document.getElementById("conteudoModal");
                if (!modalContent) {
                    return;
                }

                this.CarregandoDetalhes = true;
                modalContent.setAttribute("aria-busy", "true");
                modalContent.innerHTML = '<div class="fighter-details-loading"><i class="fa fa-circle-o-notch fa-spin" aria-hidden="true"></i> Carregando perfil...</div>';
                $("#detalhesModal").modal("show");

                try {
                    var response = await fetch("/ConsLutador/DetalhesLutador?id=" + encodeURIComponent(id));
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
                    modalContent.innerHTML = '<div class="fighter-details-loading">N\u00e3o foi poss\u00edvel carregar o perfil.</div>';
                    this.notificarErro("N\u00e3o foi poss\u00edvel carregar os detalhes do lutador.");
                } finally {
                    this.CarregandoDetalhes = false;
                }
            },
            carregarUniversos: function (selecionarNome) {
                var self = this;
                if (!self.LutadorSelecionado) {
                    return;
                }

                self.CarregandoUniversos = true;
                $.ajax({
                    type: "POST",
                    url: "/Torre/GetUniversosElegiveis",
                    data: { lutadorId: self.LutadorSelecionado.Id },
                    success: function (data) {
                        if (!data || !data.sucesso) {
                            self.notificarErro(data && data.mensagem);
                            return;
                        }

                        self.Universos = data.universos || [];
                        if (selecionarNome) {
                            self.UniversoSelecionado = self.Universos.find(function (universo) {
                                return universo.Nome === selecionarNome;
                            }) || null;
                        }
                    },
                    error: function () {
                        self.notificarErro("N\u00e3o foi poss\u00edvel carregar os universos.");
                    },
                    complete: function () {
                        self.CarregandoUniversos = false;
                    }
                });
            },
            selecionarUniverso: function (universo) {
                this.UniversoSelecionado = universo;
            },
            criarTorre: function () {
                var self = this;
                if (!self.PodeCriarTorre) {
                    return;
                }

                self.CriandoTorre = true;
                $.ajax({
                    type: "POST",
                    url: "/Torre/Criar",
                    data: {
                        lutadorId: self.LutadorSelecionado.Id,
                        universo: self.UniversoSelecionado.Nome
                    },
                    success: function (data) {
                        if (!data || !data.sucesso) {
                            self.notificarErro(data && data.mensagem);
                            return;
                        }

                        self.Torre = data.torre;
                        window.localStorage.setItem(STORAGE_KEY, self.Torre.Id);
                        self.notificarSucesso("Torre gerada e salva. A ordem dos advers\u00e1rios est\u00e1 congelada.");
                    },
                    error: function () {
                        self.notificarErro("N\u00e3o foi poss\u00edvel gerar a torre.");
                    },
                    complete: function () {
                        self.CriandoTorre = false;
                    }
                });
            },
            iniciarAndar: function () {
                var self = this;
                if (!self.Torre || !self.ProximoAndar || self.ExecutandoAndar) {
                    return;
                }

                self.ExecutandoAndar = true;
                $.ajax({
                    type: "POST",
                    url: "/Torre/IniciarAndar",
                    data: { torreId: self.Torre.Id },
                    timeout: 0,
                    success: function (data) {
                        if (!data || !data.sucesso) {
                            self.notificarErro(data && data.mensagem);
                            return;
                        }

                        self.Torre = data.torre;
                        self.exibirResultado(data.resultado);
                        self.carregarUniversos(self.Torre.UniversoAlvo);
                    },
                    error: function () {
                        self.notificarErro("N\u00e3o foi poss\u00edvel concluir o andar. Ele continuar\u00e1 dispon\u00edvel para nova tentativa.");
                    },
                    complete: function () {
                        self.ExecutandoAndar = false;
                    }
                });
            },
            restaurarTorre: function () {
                var self = this;
                var id = Number(window.localStorage.getItem(STORAGE_KEY));
                if (!id) {
                    return;
                }

                $.ajax({
                    type: "POST",
                    url: "/Torre/Obter",
                    data: { id: id },
                    success: function (data) {
                        if (!data || !data.sucesso || !data.torre) {
                            window.localStorage.removeItem(STORAGE_KEY);
                            return;
                        }

                        self.Torre = data.torre;
                        self.LutadorId = data.torre.IdLutador;
                        self.carregarUniversos(data.torre.UniversoAlvo);
                    }
                });
            },
            exibirResultado: function (resultado) {
                if (!resultado) {
                    return;
                }

                var placar = resultado.PontosLutadorCasa + " x " + resultado.PontosLutadorVisitante;
                if (this.Torre.Status === "Concluida") {
                    this.notificarSucesso("Vit\u00f3ria por " + placar + ". Ins\u00edgnia Bronze conquistada!");
                } else if (this.Torre.Status === "Fracassada") {
                    this.notificarErro("Derrota por " + placar + ". A tentativa foi encerrada.");
                } else {
                    this.notificarSucesso("Andar vencido por " + placar + ". Pr\u00f3ximo advers\u00e1rio liberado.");
                }
            },
            classeAndar: function (andar) {
                return {
                    "torre-floor--pending": andar.Status === "Pendente",
                    "torre-floor--won": andar.Status === "Vencido",
                    "torre-floor--lost": andar.Status === "Derrotado",
                    "torre-floor--current": this.ProximoAndar && this.ProximoAndar.Id === andar.Id
                };
            },
            iconeAndar: function (andar) {
                if (andar.Status === "Vencido") {
                    return "fa fa-check-circle";
                }

                if (andar.Status === "Derrotado") {
                    return "fa fa-times-circle";
                }

                return this.ProximoAndar && this.ProximoAndar.Id === andar.Id
                    ? "fa fa-play-circle"
                    : "fa fa-lock";
            },
            textoAndar: function (andar) {
                if (andar.Status === "Vencido") {
                    return "Vencido";
                }

                if (andar.Status === "Derrotado") {
                    return "Derrota";
                }

                return this.ProximoAndar && this.ProximoAndar.Id === andar.Id
                    ? "Dispon\u00edvel"
                    : "Bloqueado";
            },
            estiloUniverso: function (imagem) {
                return {
                    "--torre-universe-image": "url('" + String(imagem || "/Imagens/Undefined.png").replace(/'/g, "%27") + "')"
                };
            },
            usarImagemIndefinida: function (event) {
                if (event && event.target && event.target.src.indexOf("Undefined.png") < 0) {
                    event.target.src = "/Imagens/Undefined.png";
                }
            },
            notificarSucesso: function (mensagem) {
                if (window.toastr) {
                    toastr.success(mensagem);
                }
            },
            notificarErro: function (mensagem) {
                if (window.toastr) {
                    toastr.error(mensagem || "Ocorreu um erro na Torre de Universos.");
                }
            }
        },
        mounted: function () {
            this.carregarLutadores();
        }
    });
})();

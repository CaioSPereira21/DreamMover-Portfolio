var appConsLutador = new Vue({
    el: "#consLutadorApp",
    mixins: [window.DreamMoverLutadorEditorMixin],
    data: {
        Lutadores: [],
        Lutador: '',
        NomeRival: '',
        ImportandoStandId: null,
        ImportandoStandUsaAct: null,
        ImportandoStandModoCanvas: false,
        ImportandoPortraitId: null,
        AtualizandoUniversoId: null,
        CarregandoDetalhes: false,
        PersonagensChars: [],
        PersonagensSelecionados: [],
        FiltroPersonagemCadastro: "",
        NovoPersonagemUniverso: "",
        CarregandoPersonagens: false,
        CadastrandoPersonagens: false,
        CharTestNome: "",
        CharTestArquivos: [],
        CharTestToken: "",
        CharTestEnvioToken: "",
        CharTestProgresso: 0,
        CharTestStatus: "",
        CharTestCandidatos: [],
        CharTestResultado: null,
        CharTestEscolhidoId: "",
        CharTestSobrescrever: false,
        CharTestAnalisando: false,
        CharTestExecutando: false,
        CharTestImportando: false,
        ModoVisualizacao: "cards",
        PaginaAtual: 1,
        ItensPorPagina: 12,
        OpcoesItensPorPagina: [8, 12, 24, 48],
        FiltroLutadores: "",
        FiltroUniverso: "",
        FiltroAutor: "",
        FiltroTagId: "",
        FiltroDivisao: "",
        NivelMinimo: "",
        NivelMaximo: "",
        OrdenacaoCampo: "nome",
        OrdenacaoDirecao: "asc",
        ExcluindoLutadorId: null,
    },
    methods: {
        getLutadores: function () {
            var self = this;

            $.ajax({
                type: "POST",
                url: "/ConsLutador/GetLutadores",
                success: function (data) {
                    self.Lutadores = data && data.lutadores ? data.lutadores : [];
                    self.ajustarPaginaAtual();
                },
                error: function () {
                    if (window.toastr) {
                        toastr.error("Nao foi possivel carregar os lutadores.");
                    }
                }
            });
        },
        alterarModoVisualizacao: function (modo) {
            if (modo !== "cards" && modo !== "tabela") {
                return;
            }

            this.ModoVisualizacao = modo;
            this.salvarPreferencia("dreamMover.modoConsultaLutadores", modo);
        },
        alterarTamanhoPagina: function () {
            this.PaginaAtual = 1;
            this.salvarPreferencia("dreamMover.itensConsultaLutadores", String(this.ItensPorPagina));
        },
        irParaPagina: function (pagina) {
            var paginaDestino = Math.min(Math.max(Number(pagina) || 1, 1), this.TotalPaginas);
            var scrollX = window.pageXOffset || document.documentElement.scrollLeft || 0;
            var scrollY = window.pageYOffset || document.documentElement.scrollTop || 0;
            this.PaginaAtual = paginaDestino;

            this.$nextTick(function () {
                window.scrollTo(scrollX, scrollY);
            });
        },
        reiniciarPaginacao: function () {
            this.PaginaAtual = 1;
        },
        limparFiltros: function () {
            this.FiltroLutadores = "";
            this.FiltroUniverso = "";
            this.FiltroAutor = "";
            this.FiltroTagId = "";
            this.FiltroDivisao = "";
            this.NivelMinimo = "";
            this.NivelMaximo = "";
            this.PaginaAtual = 1;
        },
        alternarDirecaoOrdenacao: function () {
            this.OrdenacaoDirecao = this.OrdenacaoDirecao === "asc" ? "desc" : "asc";
        },
        ordenarPor: function (campo) {
            if (this.OrdenacaoCampo === campo) {
                this.alternarDirecaoOrdenacao();
                return;
            }

            this.OrdenacaoCampo = campo;
            this.OrdenacaoDirecao = "asc";
        },
        ariaOrdenacao: function (campo) {
            if (this.OrdenacaoCampo !== campo) {
                return "none";
            }

            return this.OrdenacaoDirecao === "desc" ? "descending" : "ascending";
        },
        iconeOrdenacao: function (campo) {
            if (this.OrdenacaoCampo !== campo) {
                return "fa-sort";
            }

            return this.OrdenacaoDirecao === "desc" ? "fa-sort-amount-desc" : "fa-sort-amount-asc";
        },
        tituloOrdenacao: function (campo, rotulo) {
            if (this.OrdenacaoCampo !== campo) {
                return "Ordenar por " + rotulo + " em ordem crescente";
            }

            return "Ordenado por " + rotulo + " em ordem "
                + (this.OrdenacaoDirecao === "desc" ? "decrescente" : "crescente")
                + ". Clique para inverter.";
        },
        normalizarTexto: function (valor) {
            var texto = String(valor || "").toLowerCase();

            if (typeof texto.normalize === "function") {
                texto = texto.normalize("NFD").replace(/[\u0300-\u036f]/g, "");
            }

            return texto.trim();
        },
        ajustarPaginaAtual: function () {
            if (this.PaginaAtual > this.TotalPaginas) {
                this.PaginaAtual = this.TotalPaginas;
            }

            if (this.PaginaAtual < 1) {
                this.PaginaAtual = 1;
            }
        },
        salvarPreferencia: function (chave, valor) {
            try {
                window.localStorage.setItem(chave, valor);
            } catch (e) {
                // A preferencia e opcional quando o armazenamento do navegador estiver indisponivel.
            }
        },
        carregarPreferenciasVisualizacao: function () {
            try {
                var modoSalvo = window.localStorage.getItem("dreamMover.modoConsultaLutadores");
                var itensSalvos = Number(window.localStorage.getItem("dreamMover.itensConsultaLutadores"));

                if (modoSalvo === "cards" || modoSalvo === "tabela") {
                    this.ModoVisualizacao = modoSalvo;
                }

                if (this.OpcoesItensPorPagina.indexOf(itensSalvos) >= 0) {
                    this.ItensPorPagina = itensSalvos;
                }
            } catch (e) {
                // Mantem os valores padrao quando o armazenamento estiver indisponivel.
            }
        },
        solicitarExclusaoLutador: function (lutador) {
            if (!lutador || this.ExcluindoLutadorId !== null) {
                return;
            }

            var self = this;
            $.ajax({
                type: "POST",
                url: "/ConsLutador/GetImpactoExclusao",
                data: { id: lutador.Id },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) {
                            toastr.error(data && data.mensagem ? data.mensagem : "Nao foi possivel verificar a exclusao.");
                        }
                        return;
                    }

                    var partidas = data.partidasRelacionadas || 0;
                    var mensagem = "Esta acao nao podera ser desfeita.";
                    if (partidas > 0) {
                        mensagem += " " + partidas + " partida(s) relacionada(s) tambem sera(ao) excluida(s).";
                    }

                    if (!window.Swal) {
                        if (window.confirm("Excluir " + lutador.Nome + "? " + mensagem)) {
                            self.confirmarExclusaoLutador(lutador);
                        }
                        return;
                    }

                    Swal.fire({
                        title: "Excluir " + lutador.Nome + "?",
                        text: mensagem,
                        icon: "warning",
                        showCancelButton: true,
                        confirmButtonText: "Sim, excluir",
                        cancelButtonText: "Cancelar",
                        confirmButtonColor: "#b7242a",
                        focusCancel: true
                    }).then(function (resultado) {
                        if (resultado.isConfirmed) {
                            self.confirmarExclusaoLutador(lutador);
                        }
                    });
                },
                error: function () {
                    if (window.toastr) {
                        toastr.error("Nao foi possivel verificar a exclusao.");
                    }
                }
            });
        },
        confirmarExclusaoLutador: function (lutador) {
            var self = this;
            self.ExcluindoLutadorId = lutador.Id;

            $.ajax({
                type: "POST",
                url: "/ConsLutador/ExcluirLutador",
                data: { id: lutador.Id },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) {
                            toastr.error(data && data.mensagem ? data.mensagem : "Nao foi possivel excluir o lutador.");
                        }
                        return;
                    }

                    self.Lutadores = self.Lutadores.filter(function (item) {
                        return item.Id !== lutador.Id;
                    });
                    self.ajustarPaginaAtual();

                    if (window.toastr) {
                        var partidasExcluidas = data.partidasExcluidas || 0;
                        toastr.success("Lutador excluido" + (partidasExcluidas ? " com " + partidasExcluidas + " partida(s)." : "."));
                    }
                },
                error: function () {
                    if (window.toastr) {
                        toastr.error("Nao foi possivel excluir o lutador.");
                    }
                },
                complete: function () {
                    self.ExcluindoLutadorId = null;
                }
            });
        },
        abrirCadastroPersonagens: function () {
            var self = this;

            var abrirModal = function () {
                $("#modal-cadastrar-personagens").modal("show");
                self.getPersonagensChars();
            };

            if (!self.Universos.length) {
                self.getUniversos(abrirModal);
                return;
            }

            abrirModal();
        },
        getPersonagensChars: function () {
            if (this.CarregandoPersonagens) {
                return;
            }

            var self = this;
            self.CarregandoPersonagens = true;

            $.ajax({
                type: "POST",
                url: "/ConsLutador/GetPersonagensCharsDisponiveis",
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) {
                            toastr.error(data && data.mensagem ? data.mensagem : "Nao foi possivel carregar os personagens.");
                        }
                        return;
                    }

                    self.PersonagensChars = data.personagens || [];
                    self.PersonagensSelecionados = self.PersonagensSelecionados.filter(function (pasta) {
                        return self.PersonagensChars.some(function (personagem) {
                            return personagem.Pasta === pasta;
                        });
                    });
                },
                error: function () {
                    if (window.toastr) {
                        toastr.error("Nao foi possivel carregar os personagens.");
                    }
                },
                complete: function () {
                    self.CarregandoPersonagens = false;
                }
            });
        },
        selecionarTodosPersonagens: function () {
            this.PersonagensSelecionados = this.PersonagensCharsFiltrados.map(function (personagem) {
                return personagem.Pasta;
            });
        },
        limparSelecaoPersonagens: function () {
            this.PersonagensSelecionados = [];
        },
        cadastrarPersonagensDosChars: function () {
            if (this.CadastrandoPersonagens) {
                return;
            }

            if (!this.NovoPersonagemUniverso) {
                if (window.toastr) {
                    toastr.warning("Selecione o universo dos personagens.");
                }
                return;
            }

            if (!this.PersonagensSelecionados.length) {
                if (window.toastr) {
                    toastr.warning("Selecione pelo menos um personagem.");
                }
                return;
            }

            var self = this;
            self.CadastrandoPersonagens = true;

            $.ajax({
                type: "POST",
                url: "/ConsLutador/CadastrarPersonagensDosChars",
                traditional: true,
                data: {
                    universo: self.NovoPersonagemUniverso,
                    pastasPersonagem: self.PersonagensSelecionados
                },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) {
                            toastr.error(data && data.mensagem ? data.mensagem : "Nao foi possivel cadastrar os personagens.");
                        }
                        return;
                    }

                    if (window.toastr) {
                        toastr.success((data.cadastrados || 0) + " personagem(ns) cadastrado(s).");
                    }

                    self.PersonagensSelecionados = [];
                    self.getLutadores();
                    self.getPersonagensChars();
                },
                error: function () {
                    if (window.toastr) {
                        toastr.error("Nao foi possivel cadastrar os personagens.");
                    }
                },
                complete: function () {
                    self.CadastrandoPersonagens = false;
                }
            });
        },
        abrirTestePacotesChars: function () {
            if (this.CharTestToken) {
                this.cancelarTesteCharsSilencioso();
            }
            this.limparEstadoTesteChars();
            $("#modal-testar-chars").modal("show");
        },
        selecionarPacotesChars: function (event) {
            var arquivos = Array.prototype.slice.call(event && event.target && event.target.files ? event.target.files : []);
            var invalidos = arquivos.some(function (arquivo) {
                return !/\.(zip|rar)$/i.test(arquivo.name || "") || !arquivo.size;
            });
            if (invalidos) {
                if (window.toastr) toastr.warning("Selecione somente pacotes .zip ou .rar não vazios.");
                this.CharTestArquivos = [];
                if (event && event.target) event.target.value = "";
                return;
            }
            if (arquivos.length > 6) {
                if (window.toastr) toastr.warning("Compare no máximo 6 personagens por torneio.");
                arquivos = arquivos.slice(0, 6);
            }
            this.CharTestArquivos = arquivos;
        },
        analisarPacotesChars: async function () {
            if (this.CharTestAnalisando) return;
            if (!this.CharTestNome) {
                if (window.toastr) toastr.warning("Informe o nome obrigatório do personagem.");
                return;
            }
            if (this.CharTestArquivos.length < 2) {
                if (window.toastr) toastr.warning("Selecione ao menos dois pacotes para comparar.");
                return;
            }
            var self = this;
            var tamanhoTotal = self.CharTestArquivos.reduce(function (total, arquivo) { return total + arquivo.size; }, 0);
            var enviados = 0;
            var tamanhoParte = 8 * 1024 * 1024;
            self.CharTestAnalisando = true;
            self.CharTestProgresso = 0;
            self.CharTestStatus = "Preparando envio...";
            try {
                var inicio = await $.ajax({
                    type: "POST",
                    url: "/ConsLutador/IniciarEnvioPacotesChars",
                    data: { arquivos: JSON.stringify(self.CharTestArquivos.map(function (arquivo) {
                        return { FileName: arquivo.name, Length: arquivo.size };
                    })) }
                });
                if (!inicio || !inicio.sucesso) throw new Error(inicio && inicio.mensagem ? inicio.mensagem : "Não foi possível iniciar o envio.");
                self.CharTestEnvioToken = inicio.token;

                for (var indice = 0; indice < self.CharTestArquivos.length; indice++) {
                    var arquivo = self.CharTestArquivos[indice];
                    self.CharTestStatus = "Enviando " + arquivo.name + " (" + (indice + 1) + "/" + self.CharTestArquivos.length + ")...";
                    for (var offset = 0; offset < arquivo.size; offset += tamanhoParte) {
                        var parte = arquivo.slice(offset, Math.min(offset + tamanhoParte, arquivo.size));
                        var formulario = new FormData();
                        formulario.append("token", self.CharTestEnvioToken);
                        formulario.append("indice", indice);
                        formulario.append("offset", offset);
                        formulario.append("parte", parte, arquivo.name);
                        var respostaParte = await $.ajax({
                            type: "POST",
                            url: "/ConsLutador/EnviarPartePacoteChar",
                            data: formulario,
                            processData: false,
                            contentType: false,
                            timeout: 0
                        });
                        if (!respostaParte || !respostaParte.sucesso || respostaParte.recebido !== offset + parte.size)
                            throw new Error(respostaParte && respostaParte.mensagem ? respostaParte.mensagem : "Falha ao enviar parte do pacote.");
                        enviados += parte.size;
                        self.CharTestProgresso = Math.round(enviados * 100 / tamanhoTotal);
                    }
                }
                self.CharTestStatus = "Analisando os pacotes no servidor...";
                var analise = await $.ajax({
                    type: "POST",
                    url: "/ConsLutador/AnalisarPacotesChars",
                    data: { nome: self.CharTestNome, tokenEnvio: self.CharTestEnvioToken },
                    timeout: 0
                });
                if (!analise || !analise.sucesso)
                    throw new Error(analise && analise.mensagem ? analise.mensagem : "Não foi possível analisar os personagens.");
                self.CharTestEnvioToken = "";
                self.CharTestToken = analise.token || "";
                self.CharTestNome = analise.nome || self.CharTestNome;
                self.CharTestCandidatos = analise.candidatos || [];
                if (window.toastr) toastr.info("Pacotes validados. Confira as versões antes de iniciar as lutas.");
            } catch (erro) {
                if (window.toastr) toastr.error(erro.message || self.mensagemErroCharTest(erro, "Não foi possível analisar os personagens."));
                if (self.CharTestEnvioToken) {
                    $.post("/ConsLutador/CancelarTesteChars", { token: self.CharTestEnvioToken });
                    self.CharTestEnvioToken = "";
                }
            } finally {
                self.CharTestAnalisando = false;
                self.CharTestStatus = "";
            }
        },
        executarTorneioChars: function () {
            if (!this.CharTestToken || this.CharTestExecutando) return;
            var self = this;
            self.CharTestExecutando = true;
            $.ajax({
                type: "POST",
                url: "/ConsLutador/ExecutarTorneioChars",
                data: { token: self.CharTestToken },
                timeout: 0,
                success: function (data) {
                    if (!data || !data.sucesso || !data.resultado) {
                        if (window.toastr) toastr.error(data && data.mensagem ? data.mensagem : "Não foi possível concluir o torneio.");
                        return;
                    }
                    self.CharTestResultado = data.resultado;
                    self.CharTestEscolhidoId = data.resultado.RecomendadoId || "";
                    if (window.toastr) toastr.success(data.mensagem || "Torneio concluído.");
                },
                error: function (xhr) {
                    if (window.toastr) toastr.error(self.mensagemErroCharTest(xhr, "Não foi possível concluir o torneio."));
                },
                complete: function () {
                    self.CharTestExecutando = false;
                }
            });
        },
        importarPacoteCharEscolhido: function () {
            if (!this.CharTestToken || !this.CharTestEscolhidoId || this.CharTestImportando) return;
            var self = this;
            self.CharTestImportando = true;
            $.ajax({
                type: "POST",
                url: "/ConsLutador/ImportarPacoteChar",
                data: {
                    token: self.CharTestToken,
                    candidatoId: self.CharTestEscolhidoId,
                    nome: self.CharTestNome,
                    sobrescrever: self.CharTestSobrescrever
                },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) toastr.error(data && data.mensagem ? data.mensagem : "Não foi possível instalar o personagem.");
                        return;
                    }
                    self.CharTestToken = "";
                    if (window.toastr) toastr.success(data.mensagem || "Personagem instalado em chars.");
                    $("#modal-testar-chars").modal("hide");
                    self.getPersonagensChars();
                },
                error: function (xhr) {
                    if (window.toastr) toastr.error(self.mensagemErroCharTest(xhr, "Não foi possível instalar o personagem."));
                },
                complete: function () {
                    self.CharTestImportando = false;
                }
            });
        },
        reiniciarTestePacotesChars: function () {
            if (this.CharTestExecutando || this.CharTestImportando) return;
            this.cancelarTesteCharsSilencioso();
            this.limparEstadoTesteChars();
        },
        cancelarTesteCharsSilencioso: function () {
            var token = this.CharTestToken;
            var envioToken = this.CharTestEnvioToken;
            this.CharTestToken = "";
            this.CharTestEnvioToken = "";
            if (token) $.post("/ConsLutador/CancelarTesteChars", { token: token });
            if (envioToken) $.post("/ConsLutador/CancelarTesteChars", { token: envioToken });
        },
        limparEstadoTesteChars: function () {
            this.CharTestNome = "";
            this.CharTestArquivos = [];
            this.CharTestToken = "";
            this.CharTestEnvioToken = "";
            this.CharTestProgresso = 0;
            this.CharTestStatus = "";
            this.CharTestCandidatos = [];
            this.CharTestResultado = null;
            this.CharTestEscolhidoId = "";
            this.CharTestSobrescrever = false;
            this.CharTestAnalisando = false;
            this.CharTestExecutando = false;
            this.CharTestImportando = false;
            if (this.$refs.charTestArquivos) this.$refs.charTestArquivos.value = "";
        },
        formatarTamanhoChar: function (bytes) {
            var tamanho = Number(bytes || 0);
            if (tamanho < 1024) return tamanho + " B";
            if (tamanho < 1024 * 1024) return (tamanho / 1024).toFixed(1) + " KB";
            return (tamanho / (1024 * 1024)).toFixed(1) + " MB";
        },
        mensagemErroCharTest: function (xhr, padrao) {
            return xhr && xhr.responseJSON && xhr.responseJSON.mensagem ? xhr.responseJSON.mensagem : padrao;
        },
        getDetalhesLutador: async function (lutador) {
            if (!lutador || !lutador.Id || this.CarregandoDetalhes) {
                return;
            }

            var self = this;
            self.NomeRival = "";
            self.CarregandoDetalhes = true;

            try {
                var modalContent = document.getElementById("conteudoModal");
                modalContent.innerHTML = "<div class='fighter-details-loading'>Carregando...</div>";

                var response = await fetch("/ConsLutador/DetalhesLutador?id=" + lutador.Id);
                var html = await response.text();

                modalContent.innerHTML = html;

                var scriptAnterior = document.getElementById("detalhesLutadorScriptModal");
                if (scriptAnterior) {
                    scriptAnterior.parentNode.removeChild(scriptAnterior);
                }

                var script = document.createElement("script");
                script.id = "detalhesLutadorScriptModal";
                script.src = "/Content/js/Views/DetalhesLutador/Index.js?modal=" + Date.now();
                document.body.appendChild(script);

                $("#modal-detalhes").modal("show");
            } catch (error) {
                if (window.toastr) {
                    toastr.error("Nao foi possivel carregar os detalhes do lutador.");
                }
            } finally {
                self.CarregandoDetalhes = false;
            }
        },
        selecionarUniverso: function (lutador) {
            if (!lutador || this.AtualizandoUniversoId) {
                return;
            }

            var self = this;

            if (!self.Universos.length) {
                self.getUniversos(function () {
                    self.abrirSelecaoUniverso(lutador);
                });
                return;
            }

            self.abrirSelecaoUniverso(lutador);
        },
        abrirSelecaoUniverso: function (lutador) {
            var self = this;

            if (!window.Swal) {
                var universoDigitado = window.prompt("Universo", lutador.UniversoNome || "");
                if (universoDigitado) {
                    self.atualizarUniverso(lutador, universoDigitado);
                }
                return;
            }

            var opcoes = {};
            self.Universos.forEach(function (universo) {
                opcoes[universo.nome] = universo.nome;
            });
            opcoes["__novo__"] = "Outro...";

            Swal.fire({
                title: "Universo de " + lutador.Nome,
                input: "select",
                inputOptions: opcoes,
                inputValue: lutador.UniversoNome || "",
                inputPlaceholder: "Selecione um universo",
                showCancelButton: true,
                confirmButtonText: "Salvar",
                cancelButtonText: "Cancelar"
            }).then(function (resultado) {
                if (!resultado.isConfirmed || !resultado.value) {
                    return;
                }

                if (resultado.value === "__novo__") {
                    self.digitarUniverso(lutador);
                    return;
                }

                self.atualizarUniverso(lutador, resultado.value);
            });
        },
        digitarUniverso: function (lutador) {
            var self = this;

            Swal.fire({
                title: "Novo universo",
                input: "text",
                inputValue: lutador.UniversoNome || "",
                inputPlaceholder: "Nome do arquivo em /Imagens, sem .png",
                showCancelButton: true,
                confirmButtonText: "Salvar",
                cancelButtonText: "Cancelar"
            }).then(function (resultado) {
                if (resultado.isConfirmed && resultado.value) {
                    self.atualizarUniverso(lutador, resultado.value);
                }
            });
        },
        atualizarUniverso: function (lutador, universo) {
            if (!lutador || !universo || this.AtualizandoUniversoId) {
                return;
            }

            var self = this;
            self.AtualizandoUniversoId = lutador.Id;

            $.ajax({
                type: "POST",
                url: "/ConsLutador/AtualizarUniverso",
                data: {
                    id: lutador.Id,
                    universo: universo
                },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) {
                            toastr.error(data && data.mensagem ? data.mensagem : "Nao foi possivel atualizar o universo.");
                        }
                        return;
                    }

                    var universoUrl = data.universo + "?v=" + new Date().getTime();
                    lutador.UniversoNome = data.universoNome;
                    lutador.Universo = universoUrl;

                    if (self.Lutador && self.Lutador.Id === lutador.Id) {
                        self.Lutador.UniversoNome = data.universoNome;
                        self.Lutador.Universo = universoUrl;
                    }

                    self.getUniversos();

                    if (window.toastr) {
                        toastr.success("Universo atualizado.");
                    }
                },
                error: function () {
                    if (window.toastr) {
                        toastr.error("Nao foi possivel atualizar o universo.");
                    }
                },
                complete: function () {
                    self.AtualizandoUniversoId = null;
                }
            });
        },
        importarStand: function (lutador, usarPaletaAct, preservarCanvasSprite) {
            if (!lutador || this.ImportandoStandId) {
                return;
            }

            var self = this;
            var manterCanvas = !!preservarCanvasSprite;
            self.ImportandoStandId = lutador.Id;
            self.ImportandoStandUsaAct = usarPaletaAct;
            self.ImportandoStandModoCanvas = manterCanvas;

            $.ajax({
                type: "POST",
                url: "/ConsLutador/ImportarStand",
                data: {
                    id: lutador.Id,
                    sobrescrever: true,
                    usarPaletaAct: usarPaletaAct,
                    padding: 0,
                    preservarCanvasSprite: manterCanvas
                },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) {
                            toastr.error(data && data.mensagem ? data.mensagem : "Nao foi possivel importar o stand.");
                        }
                        return;
                    }

                    var standUrl = data.stand;
                    lutador.Stand = standUrl;

                    if (self.Lutador && self.Lutador.Id === lutador.Id) {
                        self.Lutador.Stand = standUrl;
                    }

                    if (window.toastr) {
                        if (manterCanvas) {
                            toastr.success("Stand importado mantendo o canvas.");
                        } else {
                            toastr.success(usarPaletaAct ? "Stand importado com paleta ACT." : "Stand importado sem paleta ACT.");
                        }
                    }
                },
                error: function () {
                    if (window.toastr) {
                        toastr.error("Nao foi possivel importar o stand.");
                    }
                },
                complete: function () {
                    self.ImportandoStandId = null;
                    self.ImportandoStandUsaAct = null;
                    self.ImportandoStandModoCanvas = false;
                }
            });
        },
        importarPortrait: function (lutador) {
            if (!lutador || this.ImportandoPortraitId) {
                return;
            }

            var self = this;
            self.ImportandoPortraitId = lutador.Id;

            $.ajax({
                type: "POST",
                url: "/ConsLutador/ImportarPortrait",
                data: {
                    id: lutador.Id,
                    sobrescrever: true,
                    usarPaletaAct: true,
                    grupo: 9000,
                    imagem: 0
                },
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) {
                            toastr.error(data && data.mensagem ? data.mensagem : "Nao foi possivel importar o portrait.");
                        }
                        return;
                    }

                    var portraitUrl = data.portrait;
                    lutador.Portrait = portraitUrl;

                    if (self.Lutador && self.Lutador.Id === lutador.Id) {
                        self.Lutador.Portrait = portraitUrl;
                    }

                    if (window.toastr) {
                        toastr.success("Portrait importado.");
                    }
                },
                error: function () {
                    if (window.toastr) {
                        toastr.error("Nao foi possivel importar o portrait.");
                    }
                },
                complete: function () {
                    self.ImportandoPortraitId = null;
                }
            });
        },
    },
    mounted: function () {
        var self = this;

        self.carregarPreferenciasVisualizacao();
        self.getLutadores();
        self.getUniversos();
        self.getTags();
        $("#modal-testar-chars").on("hidden.bs.modal.charTest", function () {
            if (!self.CharTestAnalisando && !self.CharTestExecutando && !self.CharTestImportando) {
                self.cancelarTesteCharsSilencioso();
                self.limparEstadoTesteChars();
            }
        });
    },
    beforeDestroy: function () {
        $("#modal-testar-chars").off("hidden.bs.modal.charTest");
    },
    components: {

    },
    computed: {
        RivaisFiltrados() {
            if (this.Lutador) {
                return this.Lutador.Rivais.filter(rival => {
                    return rival.Nome.toLowerCase().includes(this.NomeRival.toLowerCase());
                });
            } else
                return [];
        },
        PersonagensCharsFiltrados() {
            var filtro = (this.FiltroPersonagemCadastro || "").toLowerCase();

            return this.PersonagensChars.filter(function (personagem) {
                if (!filtro) {
                    return true;
                }

                return (personagem.Nome || "").toLowerCase().indexOf(filtro) >= 0
                    || (personagem.Autor || "").toLowerCase().indexOf(filtro) >= 0
                    || (personagem.Pasta || "").toLowerCase().indexOf(filtro) >= 0;
            });
        },
        nomePacoteRecomendado() {
            if (!this.CharTestResultado || !this.CharTestResultado.Ranking) return "";
            var recomendado = this.CharTestResultado.Ranking.find(function (item) { return item.Recomendado; });
            return recomendado ? recomendado.Pacote : "";
        },
        UniversosDisponiveisFiltro() {
            var universos = this.Lutadores.map(function (lutador) {
                return lutador.UniversoNome || "";
            }).filter(function (universo) {
                return !!universo;
            });

            return Array.from(new Set(universos)).sort(function (a, b) {
                return a.localeCompare(b, "pt-BR", { sensitivity: "base" });
            });
        },
        AutoresDisponiveisFiltro() {
            var autores = this.Lutadores.map(function (lutador) {
                return lutador.Autor || "";
            }).filter(function (autor) {
                return !!autor;
            });

            return Array.from(new Set(autores)).sort(function (a, b) {
                return a.localeCompare(b, "pt-BR", { sensitivity: "base" });
            });
        },
        DivisoesDisponiveisFiltro() {
            var divisoes = {};

            this.Lutadores.forEach(function (lutador) {
                var nome = String(lutador.DivisaoNome || "").trim();
                if (!nome) {
                    return;
                }

                var valor = String(Number(lutador.Divisao));
                divisoes[valor] = {
                    Valor: valor,
                    Nome: nome,
                    Ordem: Number(lutador.Divisao)
                };
            });

            return Object.keys(divisoes).map(function (chave) {
                return divisoes[chave];
            }).sort(function (a, b) {
                return a.Ordem - b.Ordem;
            });
        },
        TemFiltrosAtivos() {
            return !!(this.FiltroLutadores
                || this.FiltroUniverso
                || this.FiltroAutor
                || this.FiltroTagId !== ""
                || this.FiltroDivisao !== ""
                || this.NivelMinimo !== ""
                || this.NivelMaximo !== "");
        },
        LutadoresFiltradosOrdenados() {
            var self = this;
            var termo = self.normalizarTexto(self.FiltroLutadores);
            var filtrarSemUniverso = self.FiltroUniverso === "__sem_universo__";
            var universo = filtrarSemUniverso ? "" : self.normalizarTexto(self.FiltroUniverso);
            var autor = self.normalizarTexto(self.FiltroAutor);
            var tagId = self.FiltroTagId === "" ? null : Number(self.FiltroTagId);
            var filtrarSemDivisao = self.FiltroDivisao === "__sem_divisao__";
            var divisao = self.FiltroDivisao === "" || filtrarSemDivisao ? null : Number(self.FiltroDivisao);
            var nivelMinimo = self.NivelMinimo === "" ? null : Number(self.NivelMinimo);
            var nivelMaximo = self.NivelMaximo === "" ? null : Number(self.NivelMaximo);

            var resultado = self.Lutadores.filter(function (lutador) {
                var nomeLutador = self.normalizarTexto(lutador.Nome);
                var autorLutador = self.normalizarTexto(lutador.Autor);
                var universoLutador = self.normalizarTexto(lutador.UniversoNome);
                var tagsLutador = lutador.Tags || [];
                var nomesTagsLutador = self.normalizarTexto(tagsLutador.map(function (tag) { return tag.Nome; }).join(" "));
                var possuiDivisao = !!String(lutador.DivisaoNome || "").trim();
                var nivelLutador = Number(lutador.Nivel || 1);

                if (termo && nomeLutador.indexOf(termo) < 0 && autorLutador.indexOf(termo) < 0 && nomesTagsLutador.indexOf(termo) < 0) {
                    return false;
                }
                if (filtrarSemUniverso && universoLutador) {
                    return false;
                }
                if (!filtrarSemUniverso && universo && universoLutador !== universo) {
                    return false;
                }
                if (autor && autorLutador !== autor) {
                    return false;
                }
                if (tagId !== null && !tagsLutador.some(function (tag) { return tag.Id === tagId; })) {
                    return false;
                }
                if (filtrarSemDivisao && possuiDivisao) {
                    return false;
                }
                if (!filtrarSemDivisao && divisao !== null && Number(lutador.Divisao) !== divisao) {
                    return false;
                }
                if (nivelMinimo !== null && nivelLutador < nivelMinimo) {
                    return false;
                }
                if (nivelMaximo !== null && nivelLutador > nivelMaximo) {
                    return false;
                }

                return true;
            });

            var campo = self.OrdenacaoCampo;
            var direcao = self.OrdenacaoDirecao === "desc" ? -1 : 1;
            return resultado.slice().sort(function (a, b) {
                if (campo === "nivel") {
                    var diferencaNivel = Number(a.Nivel || 1) - Number(b.Nivel || 1);
                    if (diferencaNivel !== 0) {
                        return diferencaNivel * direcao;
                    }

                    var diferencaExperiencia = Number(a.Exp || 0) - Number(b.Exp || 0);
                    if (diferencaExperiencia !== 0) {
                        return diferencaExperiencia * direcao;
                    }
                }

                if (campo === "rating") {
                    var diferencaRating = Number(a.RatingElo || 1000) - Number(b.RatingElo || 1000);
                    if (diferencaRating !== 0) {
                        return diferencaRating * direcao;
                    }
                }

                if (campo === "experiencia") {
                    var diferencaExperienciaDireta = Number(a.Exp || 0) - Number(b.Exp || 0);
                    if (diferencaExperienciaDireta !== 0) {
                        return diferencaExperienciaDireta * direcao;
                    }
                }

                if (campo === "partidas") {
                    var diferencaPartidas = Number(a.Partidas || 0) - Number(b.Partidas || 0);
                    if (diferencaPartidas !== 0) {
                        return diferencaPartidas * direcao;
                    }
                }

                if (campo === "divisao") {
                    var diferencaDivisao = Number(a.Divisao || 0) - Number(b.Divisao || 0);
                    if (diferencaDivisao !== 0) {
                        return diferencaDivisao * direcao;
                    }

                    var diferencaSequencia = Number(a.SequenciaDivisao || 0) - Number(b.SequenciaDivisao || 0);
                    if (diferencaSequencia !== 0) {
                        return diferencaSequencia * direcao;
                    }
                }

                var valorA = campo === "universo"
                    ? a.UniversoNome
                    : campo === "autor"
                        ? a.Autor
                        : campo === "tags"
                            ? (a.Tags || []).map(function (tag) { return tag.Nome; }).join(" ")
                            : a.Nome;
                var valorB = campo === "universo"
                    ? b.UniversoNome
                    : campo === "autor"
                        ? b.Autor
                        : campo === "tags"
                            ? (b.Tags || []).map(function (tag) { return tag.Nome; }).join(" ")
                            : b.Nome;
                var comparacao = String(valorA || "").localeCompare(String(valorB || ""), "pt-BR", {
                    sensitivity: "base",
                    numeric: true
                });

                if (comparacao === 0 && campo !== "nome") {
                    comparacao = String(a.Nome || "").localeCompare(String(b.Nome || ""), "pt-BR", {
                        sensitivity: "base",
                        numeric: true
                    });
                }

                return comparacao * direcao;
            });
        },
        TotalPaginas() {
            return Math.max(1, Math.ceil(this.LutadoresFiltradosOrdenados.length / this.ItensPorPagina));
        },
        LutadoresPaginados() {
            var inicio = (this.PaginaAtual - 1) * this.ItensPorPagina;
            return this.LutadoresFiltradosOrdenados.slice(inicio, inicio + this.ItensPorPagina);
        },
        IndiceInicial() {
            if (!this.LutadoresFiltradosOrdenados.length) {
                return 0;
            }

            return ((this.PaginaAtual - 1) * this.ItensPorPagina) + 1;
        },
        IndiceFinal() {
            return Math.min(this.PaginaAtual * this.ItensPorPagina, this.LutadoresFiltradosOrdenados.length);
        },
        PaginasVisiveis() {
            var total = this.TotalPaginas;
            var atual = this.PaginaAtual;

            if (total <= 7) {
                return Array.from({ length: total }, function (_, indice) {
                    return indice + 1;
                });
            }

            if (atual <= 4) {
                return [1, 2, 3, 4, 5, null, total];
            }

            if (atual >= total - 3) {
                return [1, null, total - 4, total - 3, total - 2, total - 1, total];
            }

            return [1, null, atual - 1, atual, atual + 1, null, total];
        }
    },
    watch: {
        FiltroLutadores: function () {
            this.reiniciarPaginacao();
        },
        FiltroUniverso: function () {
            this.reiniciarPaginacao();
        },
        FiltroAutor: function () {
            this.reiniciarPaginacao();
        },
        FiltroTagId: function () {
            this.reiniciarPaginacao();
        },
        FiltroDivisao: function () {
            this.reiniciarPaginacao();
        },
        NivelMinimo: function () {
            this.reiniciarPaginacao();
        },
        NivelMaximo: function () {
            this.reiniciarPaginacao();
        },
        OrdenacaoCampo: function () {
            this.reiniciarPaginacao();
        },
        OrdenacaoDirecao: function () {
            this.reiniciarPaginacao();
        }
    },
});

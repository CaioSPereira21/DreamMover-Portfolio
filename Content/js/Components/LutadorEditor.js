(function (window, $) {
    "use strict";

    window.DreamMoverLutadorEditorMixin = {
        data: function () {
            return {
                Universos: [],
                Tags: [],
                LutadorEmEdicao: null,
                LutadorOriginalEdicao: null,
                ArquivoPortraitEdicao: null,
                ArquivoStandEdicao: null,
                PreviewPortraitEdicao: "",
                PreviewStandEdicao: "",
                NovaTagNome: "",
                CadastrandoTag: false,
                VerificandoAutorPasta: false,
                VerificacaoAutorPasta: null,
                VerificacaoAutorPastaRequisicao: 0,
                SalvandoLutador: false
            };
        },
        methods: {
            formatarNumero: function (valor) {
                return Number(valor || 0).toLocaleString("pt-BR", {
                    maximumFractionDigits: 2
                });
            },
            getUniversos: function (callback) {
                var self = this;

                $.ajax({
                    type: "POST",
                    url: "/ConsLutador/GetUniversos",
                    success: function (data) {
                        if (data && data.sucesso) {
                            self.Universos = data.universos || [];
                        }
                    },
                    error: function () {
                        if (window.toastr) {
                            toastr.error("Nao foi possivel carregar os universos.");
                        }
                    },
                    complete: function () {
                        if (typeof callback === "function") {
                            callback();
                        }
                    }
                });
            },
            getTags: function (callback) {
                var self = this;

                $.ajax({
                    type: "POST",
                    url: "/ConsLutador/GetTags",
                    success: function (data) {
                        if (data && data.sucesso) {
                            self.Tags = data.tags || [];
                        }
                    },
                    error: function () {
                        if (window.toastr) {
                            toastr.error("Nao foi possivel carregar as tags.");
                        }
                    },
                    complete: function () {
                        if (typeof callback === "function") {
                            callback();
                        }
                    }
                });
            },
            abrirEdicaoLutador: function (lutador) {
                if (!lutador || this.SalvandoLutador) {
                    return;
                }

                var self = this;
                var abrirModal = function () {
                    self.LutadorOriginalEdicao = lutador;
                    self.LutadorEmEdicao = {
                        Id: lutador.Id,
                        Nome: lutador.Nome || "",
                        Autor: lutador.Autor || "",
                        UniversoNome: lutador.UniversoNome || "",
                        Nivel: lutador.Nivel || 1,
                        Exp: lutador.Exp || 0,
                        RatingElo: lutador.RatingElo || 1000,
                        AtributosCns: {
                            life: "",
                            attack: "",
                            defence: "",
                            xscale: "",
                            yscale: ""
                        },
                        AtributosCnsOriginais: {
                            life: "",
                            attack: "",
                            defence: "",
                            xscale: "",
                            yscale: ""
                        },
                        TagIds: (lutador.Tags || []).map(function (tag) { return tag.Id; })
                    };
                    self.ArquivoPortraitEdicao = null;
                    self.ArquivoStandEdicao = null;
                    self.PreviewPortraitEdicao = lutador.Portrait || "";
                    self.PreviewStandEdicao = lutador.Stand || "";
                    self.NovaTagNome = "";
                    self.VerificandoAutorPasta = false;
                    self.VerificacaoAutorPasta = null;

                    self.$nextTick(function () {
                        if (self.$refs.portraitArquivoEdicao) {
                            self.$refs.portraitArquivoEdicao.value = "";
                        }
                        if (self.$refs.standArquivoEdicao) {
                            self.$refs.standArquivoEdicao.value = "";
                        }

                        $("#modal-editar-lutador").modal("show");
                        self.verificarAutorNaPasta();
                    });
                };
                var prepararTags = function () {
                    if (!self.Tags.length) {
                        self.getTags(abrirModal);
                        return;
                    }

                    abrirModal();
                };

                if (!self.Universos.length) {
                    self.getUniversos(prepararTags);
                    return;
                }

                prepararTags();
            },
            verificarAutorNaPasta: function () {
                if (this.VerificandoAutorPasta || !this.LutadorEmEdicao) {
                    return;
                }

                var self = this;
                var idLutador = self.LutadorEmEdicao.Id;
                var requisicao = ++self.VerificacaoAutorPastaRequisicao;
                self.VerificandoAutorPasta = true;
                self.VerificacaoAutorPasta = null;

                $.ajax({
                    type: "POST",
                    url: "/ConsLutador/VerificarAutorNaPasta",
                    data: { id: idLutador },
                    success: function (data) {
                        if (!self.LutadorEmEdicao || self.LutadorEmEdicao.Id !== idLutador || self.VerificacaoAutorPastaRequisicao !== requisicao) {
                            return;
                        }

                        self.VerificacaoAutorPasta = data || {
                            sucesso: false,
                            mensagem: "Nao foi possivel verificar o personagem na pasta."
                        };
                        self.carregarAtributosCns(self.VerificacaoAutorPasta.seguranca);
                    },
                    error: function () {
                        if (!self.LutadorEmEdicao || self.LutadorEmEdicao.Id !== idLutador || self.VerificacaoAutorPastaRequisicao !== requisicao) {
                            return;
                        }

                        self.VerificacaoAutorPasta = {
                            sucesso: false,
                            mensagem: "Nao foi possivel verificar o personagem na pasta."
                        };
                    },
                    complete: function () {
                        if (self.LutadorEmEdicao && self.LutadorEmEdicao.Id === idLutador && self.VerificacaoAutorPastaRequisicao === requisicao) {
                            self.VerificandoAutorPasta = false;
                        }
                    }
                });
            },
            carregarAtributosCns: function (seguranca) {
                if (!this.LutadorEmEdicao) {
                    return;
                }

                var chaves = ["life", "attack", "defence", "xscale", "yscale"];
                var atuais = this.LutadorEmEdicao.AtributosCns;
                var originais = this.LutadorEmEdicao.AtributosCnsOriginais;
                chaves.forEach(function (chave) {
                    atuais[chave] = "";
                    originais[chave] = "";
                });

                var atributos = seguranca && seguranca.atributos ? seguranca.atributos : [];
                atributos.forEach(function (atributo) {
                    if (!atributo.editavel || chaves.indexOf(atributo.chave) < 0) {
                        return;
                    }

                    var valor = atributo.valido && atributo.valor !== null && atributo.valor !== undefined
                        ? Number(atributo.valor)
                        : "";
                    atuais[atributo.chave] = valor;
                    originais[atributo.chave] = valor;
                });
            },
            obterAtributosCnsAlterados: function () {
                var atuais = this.LutadorEmEdicao.AtributosCns;
                var originais = this.LutadorEmEdicao.AtributosCnsOriginais;
                var nomes = {
                    life: "Life",
                    attack: "Attack",
                    defence: "Defense",
                    xscale: "X Scale",
                    yscale: "Y Scale"
                };
                var alterados = {};

                for (var chave in nomes) {
                    if (!Object.prototype.hasOwnProperty.call(nomes, chave)) {
                        continue;
                    }

                    var valorAtual = atuais[chave];
                    var valorOriginal = originais[chave];
                    var atualVazio = valorAtual === "" || valorAtual === null || valorAtual === undefined;
                    var originalVazio = valorOriginal === "" || valorOriginal === null || valorOriginal === undefined;
                    if (atualVazio && originalVazio) {
                        continue;
                    }

                    if (atualVazio) {
                        return {
                            valido: false,
                            mensagem: nomes[chave] + " deve possuir um valor numerico maior que zero."
                        };
                    }

                    var numeroAtual = Number(valorAtual);
                    if (!Number.isFinite(numeroAtual) || numeroAtual <= 0) {
                        return {
                            valido: false,
                            mensagem: nomes[chave] + " deve possuir um valor numerico maior que zero."
                        };
                    }

                    if (originalVazio || numeroAtual !== Number(valorOriginal)) {
                        alterados[chave] = numeroAtual;
                    }
                }

                return { valido: true, valores: alterados };
            },
            usarAutorDetectado: function () {
                var verificacao = this.VerificacaoAutorPasta;
                if (!this.LutadorEmEdicao || !verificacao || !verificacao.autorInformadoNaPasta) {
                    return;
                }

                this.LutadorEmEdicao.Autor = verificacao.autorDetectado || "";
            },
            autorDetectadoFoiAplicado: function () {
                if (!this.LutadorEmEdicao || !this.VerificacaoAutorPasta) {
                    return false;
                }

                var autorEditado = String(this.LutadorEmEdicao.Autor || "").trim().toLocaleLowerCase();
                var autorDetectado = String(this.VerificacaoAutorPasta.autorDetectado || "").trim().toLocaleLowerCase();
                return !!autorDetectado && autorEditado === autorDetectado;
            },
            cadastrarTagEdicao: function () {
                if (this.CadastrandoTag || !this.LutadorEmEdicao) {
                    return;
                }

                var nome = String(this.NovaTagNome || "").trim();
                if (!nome) {
                    if (window.toastr) {
                        toastr.warning("Informe o nome da tag.");
                    }
                    return;
                }

                var self = this;
                self.CadastrandoTag = true;
                $.ajax({
                    type: "POST",
                    url: "/ConsLutador/CadastrarTag",
                    data: { nome: nome },
                    success: function (data) {
                        if (!data || !data.sucesso || !data.tag) {
                            if (window.toastr) {
                                toastr.error(data && data.mensagem ? data.mensagem : "Nao foi possivel cadastrar a tag.");
                            }
                            return;
                        }

                        var tag = data.tag;
                        if (!self.Tags.some(function (item) { return item.Id === tag.Id; })) {
                            self.Tags.push(tag);
                            self.Tags.sort(function (a, b) {
                                return String(a.Nome || "").localeCompare(String(b.Nome || ""), "pt-BR", { sensitivity: "base" });
                            });
                        }

                        if (self.LutadorEmEdicao.TagIds.indexOf(tag.Id) < 0) {
                            self.LutadorEmEdicao.TagIds.push(tag.Id);
                        }

                        self.NovaTagNome = "";
                        if (window.toastr) {
                            toastr.success(data.existente
                                ? "Tag existente selecionada. Salve as alteracoes para concluir o vinculo."
                                : "Tag criada e selecionada. Salve as alteracoes para concluir o vinculo.");
                        }
                    },
                    error: function () {
                        if (window.toastr) {
                            toastr.error("Nao foi possivel cadastrar a tag.");
                        }
                    },
                    complete: function () {
                        self.CadastrandoTag = false;
                    }
                });
            },
            selecionarArquivoEdicao: function (tipo, evento) {
                var input = evento && evento.target;
                var arquivo = input && input.files && input.files.length ? input.files[0] : null;
                var self = this;

                if (!arquivo) {
                    if (tipo === "portrait") {
                        self.ArquivoPortraitEdicao = null;
                        self.PreviewPortraitEdicao = self.LutadorOriginalEdicao ? self.LutadorOriginalEdicao.Portrait : "";
                    } else {
                        self.ArquivoStandEdicao = null;
                        self.PreviewStandEdicao = self.LutadorOriginalEdicao ? self.LutadorOriginalEdicao.Stand : "";
                    }
                    return;
                }

                var extensaoValida = /\.(png|jpe?g|gif|webp)$/i.test(arquivo.name || "");
                if (!extensaoValida || arquivo.size > 8 * 1024 * 1024) {
                    input.value = "";
                    if (window.toastr) {
                        toastr.warning("Selecione uma imagem PNG, JPG, GIF ou WEBP de ate 8 MB.");
                    }
                    return;
                }

                var leitor = new FileReader();
                leitor.onload = function (resultado) {
                    if (tipo === "portrait") {
                        self.PreviewPortraitEdicao = resultado.target.result;
                    } else {
                        self.PreviewStandEdicao = resultado.target.result;
                    }
                };
                leitor.readAsDataURL(arquivo);

                if (tipo === "portrait") {
                    self.ArquivoPortraitEdicao = arquivo;
                } else {
                    self.ArquivoStandEdicao = arquivo;
                }
            },
            salvarDetalhesLutador: function () {
                if (this.SalvandoLutador || !this.LutadorEmEdicao || !this.LutadorOriginalEdicao) {
                    return;
                }

                if (!String(this.LutadorEmEdicao.Nome || "").trim()) {
                    if (window.toastr) {
                        toastr.warning("Informe o nome do lutador.");
                    }
                    return;
                }

                if (!this.LutadorEmEdicao.UniversoNome) {
                    if (window.toastr) {
                        toastr.warning("Selecione o universo do lutador.");
                    }
                    return;
                }

                var atributosCns = this.obterAtributosCnsAlterados();
                if (!atributosCns.valido) {
                    if (window.toastr) {
                        toastr.warning(atributosCns.mensagem);
                    }
                    return;
                }

                var self = this;
                var formulario = new FormData();
                formulario.append("id", self.LutadorEmEdicao.Id);
                formulario.append("nome", self.LutadorEmEdicao.Nome);
                formulario.append("autor", self.LutadorEmEdicao.Autor || "");
                formulario.append("universo", self.LutadorEmEdicao.UniversoNome);
                formulario.append("tagIds", JSON.stringify(self.LutadorEmEdicao.TagIds || []));
                Object.keys(atributosCns.valores).forEach(function (chave) {
                    formulario.append(chave, atributosCns.valores[chave]);
                });

                if (self.ArquivoPortraitEdicao) {
                    formulario.append("portraitArquivo", self.ArquivoPortraitEdicao);
                }
                if (self.ArquivoStandEdicao) {
                    formulario.append("standArquivo", self.ArquivoStandEdicao);
                }

                self.SalvandoLutador = true;
                $.ajax({
                    type: "POST",
                    url: "/ConsLutador/AtualizarDetalhesLutador",
                    data: formulario,
                    processData: false,
                    contentType: false,
                    success: function (data) {
                        if (!data || !data.sucesso || !data.lutador) {
                            if (window.toastr) {
                                toastr.error(data && data.mensagem ? data.mensagem : "Nao foi possivel atualizar o lutador.");
                            }
                            return;
                        }

                        var atualizado = data.lutador;
                        var cacheBuster = "?v=" + new Date().getTime();
                        self.LutadorOriginalEdicao.Nome = atualizado.Nome;
                        self.LutadorOriginalEdicao.Autor = atualizado.Autor;
                        self.LutadorOriginalEdicao.UniversoNome = atualizado.UniversoNome;
                        self.LutadorOriginalEdicao.Universo = atualizado.Universo + cacheBuster;
                        self.LutadorOriginalEdicao.Portrait = atualizado.Portrait;
                        self.LutadorOriginalEdicao.Stand = atualizado.Stand;
                        self.LutadorOriginalEdicao.Tags = atualizado.Tags || [];

                        $("#modal-editar-lutador").modal("hide");
                        self.getUniversos();
                        if (typeof self.ajustarPaginaAtual === "function") {
                            self.ajustarPaginaAtual();
                        }
                        if (typeof self.aoAtualizarLutador === "function") {
                            self.aoAtualizarLutador(self.LutadorOriginalEdicao);
                        }

                        if (window.toastr) {
                            toastr.success("Detalhes do lutador atualizados.");
                        }
                    },
                    error: function () {
                        if (window.toastr) {
                            toastr.error("Nao foi possivel atualizar o lutador.");
                        }
                    },
                    complete: function () {
                        self.SalvandoLutador = false;
                    }
                });
            }
        }
    };
})(window, window.jQuery);

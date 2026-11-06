function caminhoImagem(valor) {
    if (!valor) {
        return "/Imagens/Undefined.png";
    }

    if (valor.indexOf("/") === 0 || valor.indexOf("http") === 0) {
        return valor;
    }

    return "/Imagens/" + valor;
}

function lutador(id, nome, portrait, stand, universoNome, nivel, seed) {
    return {
        Id: id,
        Nome: nome,
        Portrait: caminhoImagem(portrait),
        Stand: caminhoImagem(stand),
        Universo: caminhoImagem(universoNome ? universoNome + ".png" : "Undefined.png"),
        UniversoNome: universoNome,
        Nivel: nivel,
        RatingElo: 1000,
        Seed: seed
    };
}

function obterUniversoNome(valor) {
    if (!valor) {
        return "DreamMover";
    }

    var partes = valor.split("/");
    var arquivo = partes[partes.length - 1] || valor;
    return arquivo.replace(/\.[^/.]+$/, "") || "DreamMover";
}

function normalizarLutador(lutador, index) {
    var universoNome = lutador.UniversoNome || lutador.universoNome || lutador.Tag || lutador.tag || obterUniversoNome(lutador.Universo);

    return {
        Id: lutador.Id || lutador.id || index + 1,
        Nome: lutador.Nome || lutador.nome || "A definir",
        Portrait: caminhoImagem(lutador.Portrait || lutador.portrait || lutador.imagem),
        Stand: caminhoImagem(lutador.Stand || lutador.stand || lutador.Portrait || lutador.imagem),
        Universo: caminhoImagem(lutador.Universo || lutador.universo || (universoNome ? universoNome + ".png" : null)),
        UniversoNome: universoNome,
        Nivel: lutador.Nivel || lutador.nivel || 1,
        RatingElo: lutador.RatingElo || lutador.ratingElo || 1000,
        Seed: lutador.Seed || lutador.seed || index + 1
    };
}

function criarPartida(id, fase, jogador1, jogador2, placar1, placar2) {
    return {
        id: id,
        fase: fase,
        jogador1: jogador1 || null,
        jogador2: jogador2 || null,
        placar1: placar1,
        placar2: placar2
    };
}

function criarLutadoresBase() {
    return [
        lutador(1, "Abyss", "abyssPortrait.png", "AbyssStand.gif", "Marvel vs Capcom", 9, 1),
        lutador(2, "Zeroko", "ZerokoPortrait.png", "ZerokoStand.gif", "Originals", 8, 16),
        lutador(3, "Satoru Gojo", "SatoruGojoPortrait.png", "SatoruGojoStand.gif", "Jujutsu Kaisen", 10, 8),
        lutador(4, "Madara", "MadaraPortrait.png", "MadaraStand.gif", "Naruto", 9, 9),
        lutador(5, "Ryu Autentico", "ryuautenticoPortrait.png", "RyuAutenticoStand.gif", "Street Fighter", 7, 4),
        lutador(6, "Scorpion", "ScorpionPortrait.png", "ScorpionStand.gif", "Mortal Kombat", 8, 13),
        lutador(7, "Ken Violento", "kenviolentoPortrait.png", "KenViolentStand.gif", "Street Fighter", 7, 5),
        lutador(8, "Iori", "IoriPortrait.png", "IoriStand.gif", "The King of Fighters", 8, 12),
        lutador(9, "Goku Black", "GokuBlackPortrait.png", "GokuBlackStand.gif", "Dragon Ball Z", 9, 2),
        lutador(10, "Naruto", "NarutoPortrait.png", "NarutoStand.gif", "Naruto", 7, 15),
        lutador(11, "Luffy", "LuffyPortrait.png", "LuffyStand.gif", "One Piece", 8, 7),
        lutador(12, "Seiya", "SeiyaPortrait.png", "SeiyaStand.gif", "Saint Seiya", 8, 10),
        lutador(13, "Kyo Kusanagi", "KyoKusanagiPortrait.png", "KyoKusanagiStand.gif", "The King of Fighters", 8, 3),
        lutador(14, "Spiderman", "SpidermanPortrait.png", "SpidermanStand.gif", "Marvel Comics", 7, 14),
        lutador(15, "Vegeta SSJ", "VegetaSSJPortrait.png", "VegetaSSJStand.gif", "Dragon Ball Z", 8, 6),
        lutador(16, "Sub Zero", "SubZeroPortrait.png", "SubZeroStand.gif", "Mortal Kombat", 7, 11)
    ];
}

function criarFasesIniciais(lutadoresOrigem) {
    var fallback = criarLutadoresBase();
    var lutadores = [];

    for (var i = 0; i < 16; i++) {
        lutadores.push(lutadoresOrigem && lutadoresOrigem[i] ? lutadoresOrigem[i] : fallback[i]);
    }

    return [
        {
            nome: "Oitavas",
            sigla: "R16",
            partidas: [
                criarPartida("oitavas-1", "Oitavas", lutadores[0], lutadores[1], 2, 1),
                criarPartida("oitavas-2", "Oitavas", lutadores[2], lutadores[3], 2, 0),
                criarPartida("oitavas-3", "Oitavas", lutadores[4], lutadores[5], 1, 2),
                criarPartida("oitavas-4", "Oitavas", lutadores[6], lutadores[7], 0, 2),
                criarPartida("oitavas-5", "Oitavas", lutadores[8], lutadores[9], 2, 1),
                criarPartida("oitavas-6", "Oitavas", lutadores[10], lutadores[11], 2, 0),
                criarPartida("oitavas-7", "Oitavas", lutadores[12], lutadores[13], 1, 2),
                criarPartida("oitavas-8", "Oitavas", lutadores[14], lutadores[15], 2, 1)
            ]
        },
        {
            nome: "Quartas",
            sigla: "QF",
            partidas: [
                criarPartida("quartas-1", "Quartas", lutadores[0], lutadores[2], 1, 2),
                criarPartida("quartas-2", "Quartas", lutadores[5], lutadores[7], 1, 2),
                criarPartida("quartas-3", "Quartas", lutadores[8], lutadores[10], 0, 2),
                criarPartida("quartas-4", "Quartas", lutadores[13], lutadores[14], 1, 2)
            ]
        },
        {
            nome: "Semifinal",
            sigla: "SF",
            partidas: [
                criarPartida("semifinal-1", "Semifinal", lutadores[2], lutadores[7], 2, 0),
                criarPartida("semifinal-2", "Semifinal", lutadores[10], lutadores[14], 2, 1)
            ]
        },
        {
            nome: "Final",
            sigla: "GF",
            partidas: [
                criarPartida("final-1", "Final", lutadores[2], lutadores[10], null, null)
            ]
        }
    ];
}

function criarFasesSorteadas(lutadores) {
    var fases = [
        { nome: "Oitavas", sigla: "R16", partidas: [] },
        { nome: "Quartas", sigla: "QF", partidas: [] },
        { nome: "Semifinal", sigla: "SF", partidas: [] },
        { nome: "Final", sigla: "GF", partidas: [] }
    ];

    for (var i = 0; i < 8; i++) {
        fases[0].partidas.push(criarPartida("oitavas-" + (i + 1), "Oitavas", lutadores[i * 2], lutadores[i * 2 + 1], null, null));
    }

    for (var q = 0; q < 4; q++) {
        fases[1].partidas.push(criarPartida("quartas-" + (q + 1), "Quartas", null, null, null, null));
    }

    for (var s = 0; s < 2; s++) {
        fases[2].partidas.push(criarPartida("semifinal-" + (s + 1), "Semifinal", null, null, null, null));
    }

    fases[3].partidas.push(criarPartida("final-1", "Final", null, null, null, null));
    return fases;
}

function embaralhar(lista) {
    var copia = lista.slice();

    for (var i = copia.length - 1; i > 0; i--) {
        var j = Math.floor(Math.random() * (i + 1));
        var item = copia[i];
        copia[i] = copia[j];
        copia[j] = item;
    }

    return copia;
}

function criarRecorteFase(fase, faseIndex, inicio, fim) {
    return {
        nome: fase ? fase.nome : "",
        sigla: fase ? fase.sigla : "",
        faseIndex: faseIndex,
        partidas: fase && fase.partidas ? fase.partidas.slice(inicio, fim) : []
    };
}

function normalizarTextoFiltro(valor) {
    return (valor || "").toString().trim().toLowerCase();
}

function obterNumeroNivel(valor) {
    if (valor === "" || valor === null || valor === undefined) {
        return null;
    }

    var numero = parseInt(valor, 10);
    return isNaN(numero) ? null : numero;
}

function ordenarPorRating(lutadores) {
    return embaralhar(lutadores).sort(function (a, b) {
        return (b.RatingElo || 1000) - (a.RatingElo || 1000)
            || (b.Nivel || 1) - (a.Nivel || 1);
    });
}

function distribuirPorSemente(lutadores) {
    var ordemSementes = [0, 15, 7, 8, 3, 12, 4, 11, 1, 14, 6, 9, 2, 13, 5, 10];
    return ordemSementes.map(function (indice) {
        return lutadores[indice] || null;
    });
}

var appMataMata = new Vue({
    el: "#matamataApp",
    data: {
        Lutadores: criarLutadoresBase(),
        Universos: [],
        universosSelecionados: [],
        nivelMinimo: "",
        nivelMaximo: "",
        criterioSorteio: "rating",
        fases: criarFasesIniciais(),
        confrontoSelecionadoId: "final-1",
        carregandoConfronto: false,
        carregandoDetalhes: false
    },
    computed: {
        finalPartida: function () {
            if (!this.fases.length || !this.fases[this.fases.length - 1].partidas.length) {
                return null;
            }

            return this.fases[this.fases.length - 1].partidas[0];
        },
        bracketLadoEsquerdo: function () {
            if (this.fases.length < 3) {
                return [];
            }

            return [
                criarRecorteFase(this.fases[0], 0, 0, 4),
                criarRecorteFase(this.fases[1], 1, 0, 2),
                criarRecorteFase(this.fases[2], 2, 0, 1)
            ];
        },
        bracketLadoDireito: function () {
            if (this.fases.length < 3) {
                return [];
            }

            return [
                criarRecorteFase(this.fases[2], 2, 1, 2),
                criarRecorteFase(this.fases[1], 1, 2, 4),
                criarRecorteFase(this.fases[0], 0, 4, 8)
            ];
        },
        universosDisponiveis: function () {
            var mapa = {};

            function adicionar(nome, imagem) {
                if (!nome) {
                    return;
                }

                var chave = normalizarTextoFiltro(nome);
                if (!mapa[chave]) {
                    mapa[chave] = {
                        nome: nome,
                        imagem: imagem || ("/Imagens/" + nome + ".png")
                    };
                }
            }

            this.Universos.forEach(function (universo) {
                adicionar(universo.nome, universo.imagem);
            });

            this.Lutadores.forEach(function (lutador) {
                adicionar(lutador.UniversoNome, lutador.Universo);
            });

            return Object.keys(mapa)
                .map(function (chave) { return mapa[chave]; })
                .sort(function (a, b) { return a.nome.localeCompare(b.nome); });
        },
        niveisDisponiveis: function () {
            var mapa = {};

            this.Lutadores.forEach(function (lutador) {
                var nivel = obterNumeroNivel(lutador.Nivel);
                if (nivel !== null) {
                    mapa[nivel] = true;
                }
            });

            return Object.keys(mapa)
                .map(function (nivel) { return parseInt(nivel, 10); })
                .sort(function (a, b) { return a - b; });
        },
        lutadoresFiltrados: function () {
            var universosSelecionados = this.universosSelecionados.map(normalizarTextoFiltro);
            var nivelMinimo = obterNumeroNivel(this.nivelMinimo);
            var nivelMaximo = obterNumeroNivel(this.nivelMaximo);

            return this.Lutadores.filter(function (lutador) {
                var universoOk = !universosSelecionados.length ||
                    universosSelecionados.indexOf(normalizarTextoFiltro(lutador.UniversoNome)) >= 0;
                var nivel = obterNumeroNivel(lutador.Nivel) || 0;
                var nivelMinimoOk = nivelMinimo === null || nivel >= nivelMinimo;
                var nivelMaximoOk = nivelMaximo === null || nivel <= nivelMaximo;

                return universoOk && nivelMinimoOk && nivelMaximoOk;
            });
        },
        confrontoDestaque: function () {
            var selecionado = this.buscarPartidaPorId(this.confrontoSelecionadoId);

            if (selecionado) {
                return selecionado;
            }

            for (var faseIndex = 0; faseIndex < this.fases.length; faseIndex++) {
                for (var partidaIndex = 0; partidaIndex < this.fases[faseIndex].partidas.length; partidaIndex++) {
                    var partida = this.fases[faseIndex].partidas[partidaIndex];
                    if (!this.temResultado(partida) && partida.jogador1 && partida.jogador2) {
                        return partida;
                    }
                }
            }

            return this.fases[this.fases.length - 1].partidas[0];
        },
        totalLutadores: function () {
            if (!this.fases.length) {
                return 0;
            }

            var lutadores = [];
            this.fases[0].partidas.forEach(function (partida) {
                if (partida.jogador1) {
                    lutadores.push(partida.jogador1);
                }
                if (partida.jogador2) {
                    lutadores.push(partida.jogador2);
                }
            });

            return lutadores.length;
        },
        totalPartidas: function () {
            return this.fases.reduce(function (total, fase) {
                return total + fase.partidas.length;
            }, 0);
        },
        partidasFinalizadas: function () {
            var self = this;
            return this.fases.reduce(function (total, fase) {
                return total + fase.partidas.filter(function (partida) {
                    return self.temResultado(partida);
                }).length;
            }, 0);
        },
        progressoCampeonato: function () {
            if (!this.totalPartidas) {
                return 0;
            }

            return Math.round((this.partidasFinalizadas / this.totalPartidas) * 100);
        },
        campeao: function () {
            return this.getVencedor(this.finalPartida);
        },
        podeIniciarConfronto: function () {
            return !this.carregandoConfronto &&
                this.confrontoDestaque &&
                this.confrontoDestaque.jogador1 &&
                this.confrontoDestaque.jogador2 &&
                !this.temResultado(this.confrontoDestaque);
        }
    },
    methods: {
        buscarPartidaPorId: function (id) {
            for (var faseIndex = 0; faseIndex < this.fases.length; faseIndex++) {
                for (var partidaIndex = 0; partidaIndex < this.fases[faseIndex].partidas.length; partidaIndex++) {
                    if (this.fases[faseIndex].partidas[partidaIndex].id === id) {
                        return this.fases[faseIndex].partidas[partidaIndex];
                    }
                }
            }

            return null;
        },
        buscarPosicaoPartida: function (partida) {
            for (var faseIndex = 0; faseIndex < this.fases.length; faseIndex++) {
                for (var partidaIndex = 0; partidaIndex < this.fases[faseIndex].partidas.length; partidaIndex++) {
                    if (this.fases[faseIndex].partidas[partidaIndex].id === partida.id) {
                        return {
                            faseIndex: faseIndex,
                            partidaIndex: partidaIndex
                        };
                    }
                }
            }

            return null;
        },
        selecionarConfronto: function (partida) {
            if (!partida) {
                return;
            }

            this.confrontoSelecionadoId = partida.id;
        },
        getNumeroPartida: function (faseIndex, partida) {
            if (!partida || !this.fases[faseIndex] || !this.fases[faseIndex].partidas) {
                return "";
            }

            for (var index = 0; index < this.fases[faseIndex].partidas.length; index++) {
                if (this.fases[faseIndex].partidas[index].id === partida.id) {
                    return index + 1;
                }
            }

            return "";
        },
        universoSelecionado: function (nome) {
            var universo = normalizarTextoFiltro(nome);

            return this.universosSelecionados.some(function (item) {
                return normalizarTextoFiltro(item) === universo;
            });
        },
        alternarUniverso: function (nome) {
            if (!nome) {
                return;
            }

            if (this.universoSelecionado(nome)) {
                this.universosSelecionados = this.universosSelecionados.filter(function (item) {
                    return normalizarTextoFiltro(item) !== normalizarTextoFiltro(nome);
                });
                return;
            }

            this.universosSelecionados.push(nome);
        },
        limparUniversos: function () {
            this.universosSelecionados = [];
        },
        limparNivel: function () {
            this.nivelMinimo = "";
            this.nivelMaximo = "";
        },
        getImagemUniverso: function (universo) {
            if (!universo) {
                return "/Imagens/Undefined.png";
            }

            return caminhoImagem(universo.imagem || universo.nome);
        },
        usarImagemIndefinida: function (event) {
            if (event && event.target && event.target.src.indexOf("Undefined.png") < 0) {
                event.target.src = "/Imagens/Undefined.png";
            }
        },
        temResultado: function (partida) {
            return partida &&
                partida.placar1 !== null &&
                partida.placar1 !== undefined &&
                partida.placar2 !== null &&
                partida.placar2 !== undefined;
        },
        getVencedor: function (partida) {
            if (!this.temResultado(partida) || partida.placar1 === partida.placar2) {
                return null;
            }

            return partida.placar1 > partida.placar2 ? partida.jogador1 : partida.jogador2;
        },
        ehVencedor: function (partida, jogador) {
            var vencedor = this.getVencedor(partida);
            return vencedor && jogador && vencedor.Id === jogador.Id;
        },
        getNome: function (jogador) {
            return jogador && jogador.Nome ? jogador.Nome : "A definir";
        },
        getImagem: function (jogador) {
            return jogador && jogador.Portrait ? jogador.Portrait : "/Imagens/Undefined.png";
        },
        getStand: function (jogador) {
            if (!jogador) {
                return "/Imagens/Undefined.png";
            }

            return jogador.Stand || jogador.Portrait || "/Imagens/Undefined.png";
        },
        getSeed: function (jogador) {
            if (!jogador || !jogador.Seed) {
                return "A definir";
            }

            return "Seed " + (jogador.Seed < 10 ? "0" + jogador.Seed : jogador.Seed);
        },
        getNivel: function (jogador) {
            if (!jogador) {
                return 0;
            }

            var nivel = jogador.Nivel ? parseInt(jogador.Nivel, 10) : 1;
            return isNaN(nivel) || nivel < 1 ? 1 : nivel;
        },
        getTituloNivel: function (jogador) {
            if (!jogador) {
                return "Slot pendente";
            }

            return "Elo " + (jogador.RatingElo || 1000) + " | Nivel " + this.getNivel(jogador);
        },
        getEstrelasNivel: function (jogador) {
            var estrelas = [];
            var nivel = this.getNivel(jogador);

            for (var estrela = 1; estrela <= nivel; estrela++) {
                estrelas.push(estrela);
            }

            return estrelas;
        },
        getUniversoNome: function (jogador) {
            return jogador && jogador.UniversoNome ? jogador.UniversoNome : "DreamMover";
        },
        getUniversoImagem: function (jogador) {
            if (!jogador) {
                return "/Imagens/Undefined.png";
            }

            return jogador.Universo || caminhoImagem(this.getUniversoNome(jogador) + ".png");
        },
        getEstiloDestaque: function (jogador) {
            if (!jogador) {
                return {
                    "--featured-universe-bg": "none"
                };
            }

            return {
                "--featured-universe-bg": "url('" + this.getUniversoImagem(jogador).replace(/'/g, "%27") + "')"
            };
        },
        getSubtitulo: function (jogador) {
            if (!jogador) {
                return "Slot pendente";
            }

            return "Elo " + (jogador.RatingElo || 1000) + " | Nivel " + (jogador.Nivel || 1)
                + " | " + (jogador.UniversoNome || "DreamMover");
        },
        getPlacar: function (partida, lado) {
            if (!this.temResultado(partida)) {
                return "-";
            }

            return lado === 1 ? partida.placar1 : partida.placar2;
        },
        getStatusPartida: function (partida) {
            if (!partida || !partida.jogador1 || !partida.jogador2) {
                return "Aguardando";
            }

            if (this.temResultado(partida)) {
                return "Encerrado";
            }

            return partida.id === this.confrontoSelecionadoId ? "Em destaque" : "Pendente";
        },
        validarFiltroNivel: function () {
            var nivelMinimo = obterNumeroNivel(this.nivelMinimo);
            var nivelMaximo = obterNumeroNivel(this.nivelMaximo);

            if (nivelMinimo !== null && nivelMaximo !== null && nivelMinimo > nivelMaximo) {
                if (window.toastr) {
                    toastr.warning("O nivel minimo deve ser menor ou igual ao maximo.");
                }
                return false;
            }

            return true;
        },
        aplicarResultado: function (partida, pontosLutadorCasa, pontosLutadorVisitante) {
            var placar1 = parseInt(pontosLutadorCasa, 10);
            var placar2 = parseInt(pontosLutadorVisitante, 10);

            this.$set(partida, "placar1", isNaN(placar1) ? 0 : placar1);
            this.$set(partida, "placar2", isNaN(placar2) ? 0 : placar2);
        },
        aplicarProgressao: function (partida, progressao) {
            var self = this;

            if (!partida || !progressao) {
                return;
            }

            [
                { jogador: partida.jogador1, resultado: progressao.Casa },
                { jogador: partida.jogador2, resultado: progressao.Visitante }
            ].forEach(function (item) {
                if (!item.jogador || !item.resultado) {
                    return;
                }

                self.$set(item.jogador, "Exp", item.resultado.ExperienciaAtual);
                self.$set(item.jogador, "Nivel", item.resultado.NivelAtual);
                self.$set(item.jogador, "RatingElo", item.resultado.RatingEloAtual);

                var lutadorElenco = self.Lutadores.find(function (lutador) {
                    return lutador.Id === item.resultado.Id;
                });

                if (lutadorElenco && lutadorElenco !== item.jogador) {
                    self.$set(lutadorElenco, "Exp", item.resultado.ExperienciaAtual);
                    self.$set(lutadorElenco, "Nivel", item.resultado.NivelAtual);
                    self.$set(lutadorElenco, "RatingElo", item.resultado.RatingEloAtual);
                }
            });
        },
        getResumoProgressao: function (resultado) {
            if (!resultado) {
                return "";
            }

            var sinalElo = resultado.VariacaoRatingElo >= 0 ? "+" : "";
            var nivel = resultado.SubiuDeNivel ? " | Nivel " + resultado.NivelAtual + "!" : "";
            return resultado.Nome + ": +" + resultado.ExperienciaGanha + " XP | Elo "
                + sinalElo + resultado.VariacaoRatingElo + nivel;
        },
        avancarVencedor: function (partida) {
            var posicao = this.buscarPosicaoPartida(partida);
            var vencedor = this.getVencedor(partida);

            if (!posicao || !vencedor || posicao.faseIndex >= this.fases.length - 1) {
                return null;
            }

            var proximaFase = this.fases[posicao.faseIndex + 1];
            var proximaPartida = proximaFase.partidas[Math.floor(posicao.partidaIndex / 2)];
            var slot = posicao.partidaIndex % 2 === 0 ? "jogador1" : "jogador2";

            this.$set(proximaPartida, slot, vencedor);
            this.$set(proximaPartida, "placar1", null);
            this.$set(proximaPartida, "placar2", null);

            return proximaPartida;
        },
        selecionarProximoConfronto: function (proximaPartida) {
            if (proximaPartida && proximaPartida.jogador1 && proximaPartida.jogador2 && !this.temResultado(proximaPartida)) {
                this.confrontoSelecionadoId = proximaPartida.id;
                return;
            }

            for (var faseIndex = 0; faseIndex < this.fases.length; faseIndex++) {
                for (var partidaIndex = 0; partidaIndex < this.fases[faseIndex].partidas.length; partidaIndex++) {
                    var partida = this.fases[faseIndex].partidas[partidaIndex];

                    if (partida.jogador1 && partida.jogador2 && !this.temResultado(partida)) {
                        this.confrontoSelecionadoId = partida.id;
                        return;
                    }
                }
            }
        },
        sortearChave: function () {
            if (!this.validarFiltroNivel()) {
                return;
            }

            var origem = this.lutadoresFiltrados;
            if (!origem.length) {
                if (window.toastr) {
                    toastr.warning("Nenhum lutador encontrado para os filtros selecionados.");
                }
                return;
            }

            var listaSorteio = this.criterioSorteio === "rating"
                ? ordenarPorRating(origem)
                : embaralhar(origem);

            var lutadores = listaSorteio.slice(0, 16).map(function (item, index) {
                var lutadorSorteado = normalizarLutador(item, index);
                lutadorSorteado.Seed = index + 1;
                return lutadorSorteado;
            });

            if (this.criterioSorteio === "rating") {
                lutadores = distribuirPorSemente(lutadores);
            }

            while (lutadores.length < 16) {
                lutadores.push(null);
            }

            this.fases = criarFasesSorteadas(lutadores);
            this.confrontoSelecionadoId = "oitavas-1";

            if (window.toastr) {
                var mensagem = origem.length >= 16
                    ? "Nova chave sorteada."
                    : "Chave sorteada com " + origem.length + " lutador" + (origem.length > 1 ? "es" : "") + " e vagas pendentes.";
                toastr.info(mensagem);
            }
        },
        iniciarConfrontoDestaque: function () {
            var self = this;
            var confronto = self.confrontoDestaque;

            if (!self.podeIniciarConfronto) {
                if (window.toastr) {
                    toastr.warning("Selecione um confronto pendente com dois lutadores.");
                }
                return;
            }

            self.carregandoConfronto = true;

            $.ajax({
                type: "POST",
                url: "/MataMata/IniciarPartida",
                data: {
                    lutadorCasa: confronto.jogador1,
                    lutadorVisitante: confronto.jogador2
                },
                timeout: 0,
                success: function (data) {
                    if (!data || !data.sucesso) {
                        if (window.toastr) {
                            toastr.warning(data && data.mensagem ? data.mensagem : "O confronto nao gerou um resultado valido.");
                        }
                        return;
                    }

                    self.aplicarResultado(confronto, data.pontosLutadorCasa, data.pontosLutadorVisitante);
                    self.aplicarProgressao(confronto, data.progressao);
                    var proximaPartida = self.avancarVencedor(confronto);
                    self.selecionarProximoConfronto(proximaPartida);

                    if (window.toastr) {
                        toastr.success(
                            self.getResumoProgressao(data.progressao.Casa)
                            + "<br>"
                            + self.getResumoProgressao(data.progressao.Visitante),
                            "Resultado e Elo atualizados",
                            { timeOut: 7000, escapeHtml: false }
                        );
                    }
                },
                error: function () {
                    if (window.toastr) {
                        toastr.error("Nao foi possivel iniciar o confronto.");
                    }
                },
                complete: function () {
                    self.carregandoConfronto = false;
                }
            });
        },
        abrirModal: async function (id) {
            if (!id || this.carregandoDetalhes) {
                return;
            }

            var self = this;
            self.carregandoDetalhes = true;

            try {
                var modalContent = document.getElementById("conteudoModal");
                modalContent.innerHTML = "<div class='fighter-details-loading'>Carregando...</div>";

                var response = await fetch("/MataMata?detalhesLutadorId=" + id);
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

                $("#detalhesModal").modal("show");
            } catch (error) {
                if (window.toastr) {
                    toastr.error("Nao foi possivel carregar os detalhes do lutador.");
                }
            } finally {
                self.carregandoDetalhes = false;
            }
        },
        getLutadores: function () {
            var self = this;

            $.ajax({
                type: "POST",
                url: "/ConsLutador/GetLutadores",
                success: function (data) {
                    if (data && data.lutadores && data.lutadores.length) {
                        self.Lutadores = data.lutadores.map(function (item, index) {
                            return normalizarLutador(item, index);
                        });

                        self.fases = criarFasesIniciais(self.Lutadores);
                        self.confrontoSelecionadoId = "final-1";
                    }
                }
            });
        },
        getUniversos: function () {
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
                }
            });
        }
    },
    mounted: function () {
        this.getLutadores();
        this.getUniversos();
    }
});

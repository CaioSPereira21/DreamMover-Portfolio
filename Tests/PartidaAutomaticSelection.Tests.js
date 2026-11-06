const fs = require("fs");
const path = require("path");
const vm = require("vm");

let vueOptions = null;
const sandbox = {
    console: console,
    window: { DreamMoverLutadorEditorMixin: {} },
    Vue: function (options) {
        vueOptions = options;
        return options;
    }
};

const sourcePath = path.join(__dirname, "..", "Content", "js", "Views", "Partida", "Index.js");
vm.runInNewContext(fs.readFileSync(sourcePath, "utf8"), sandbox, { filename: sourcePath });

function assertSame(expected, actual, message) {
    if (expected !== actual) {
        throw new Error(message + " Esperado: " + expected + ". Atual: " + actual + ".");
    }
}

function withRandom(value, callback) {
    const originalRandom = sandbox.Math && sandbox.Math.random;
    sandbox.Math = Object.create(Math);
    sandbox.Math.random = function () { return value; };

    try {
        callback();
    } finally {
        if (originalRandom) {
            sandbox.Math.random = originalRandom;
        } else {
            delete sandbox.Math;
        }
    }
}

const sortear = vueOptions.methods.sortearLutadorComPrioridadeDeSequencia;
const sortearConfronto = vueOptions.methods.sortearConfrontoPorElo;
const lutadores = [
    { Id: 1, SequenciaDivisao: 0 },
    { Id: 2, SequenciaDivisao: 2 },
    { Id: 3, SequenciaDivisao: -3 }
];

withRandom(0, function () {
    assertSame(1, sortear(lutadores).Id, "Um lutador sem sequencia deve continuar participando do sorteio.");
});

withRandom(0.2, function () {
    assertSame(2, sortear(lutadores).Id, "Uma sequencia positiva deve aumentar a faixa do lutador no sorteio.");
});

withRandom(0.75, function () {
    assertSame(3, sortear(lutadores).Id, "Uma sequencia negativa deve ter a mesma prioridade baseada em magnitude.");
});

assertSame(null, sortear([]), "Uma lista vazia nao deve produzir um lutador.");

function criarContexto(modoAutomatico) {
    return {
        Lutadores: lutadores,
        ModoAutomatico: modoAutomatico,
        ModoDivisoes: false,
        DesafioDivisaoAtual: null,
        LutadorCasaBloqueado: false,
        LutadorVisitanteBloqueado: false,
        LutadorCasaManual: false,
        LutadorVisitanteManual: false,
        podeIniciarPartida: true,
        sortearLutadorComPrioridadeDeSequencia: sortear,
        encontrarAdversarioMaisProximo: function (referencia) {
            return lutadores.find(function (lutador) { return lutador.Id !== referencia.Id; });
        }
    };
}

withRandom(0.2, function () {
    const automatico = criarContexto(true);
    sortearConfronto.call(automatico, true);
    assertSame(2, automatico.LutadorCasa.Id, "O modo automatico deve usar o sorteio ponderado pela sequencia.");

    const manual = criarContexto(false);
    sortearConfronto.call(manual, true);
    assertSame(1, manual.LutadorCasa.Id, "O sorteio manual deve preservar a selecao uniforme existente.");
});

console.log("PartidaAutomaticSelection: todos os testes passaram.");

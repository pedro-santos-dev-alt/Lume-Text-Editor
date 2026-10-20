using System;
using System.IO;
using System.Windows;

namespace EditorDeTextoSimples
{
    class Programa
    {
        [STAThread]  // Necessário para Windows Forms
        static void Main()
        {
            var startupPath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
            var application = new Application();
            application.Run(new FormularioEditorDeTexto(startupPath));
        }
    }
}

// Les commentaires sont comme en C++

/*
 * 
 * Les commentaires multilignes existent aussi
 * 
 */

// Notre premier programme en C#

//using namespace std;
using System;
// Plusieurs directives using sont automatiquement incluses globalement par Visual Studio, dont System
// Cette instruction n'est pas nécessaire dans un projet VS

// Un seul type de fichier ([].cs), pas de fichiers d'en-tête
// Pas de #include

// Pas de fonction main
// On écrit les instructions principales dès le début du fichier

//Pas de setlocale

// Pour afficher dans la console
System.Console.WriteLine("Bonjour tout le monde");
//WriteLine ajoute un \n à la fin

//Write ne l'ajoute pas
Console.Write("Début de la ligne . . .");
Console.WriteLine("Fin de la ligne");

// Pas de System("pause") en C#, il faut le faire manuellement
Console.WriteLine("Appuyer sur une touche pour continuer . . .");
Console.ReadKey(true); //Lit une touche du clavier, true indique de ne pas afficher la touche lue dans la console

// system("cls")
Console.Clear();

//cw TAB
Console.WriteLine("Bienvenue au cours de \n\"Programmation orientée objet\"");
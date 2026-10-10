export interface Recipe {
  id: string;
  name: string;
  description: string;
  category: string;
  minutes: number;
  servings: number;
  rating: string;
  image: string;
  tags: string[];
  ingredients: string[];
  steps: string[];
}

export const DEMO_RECIPES: Recipe[] = [
  {
    id: 'lemon-pasta',
    name: 'Cremige Zitronenpasta',
    description: 'Frische Zitrone, Parmesan und ein Hauch Knoblauch – unkompliziertes Wohlfühlessen.',
    category: 'Vegetarisch',
    minutes: 25,
    servings: 2,
    rating: 'Einfach',
    image: '/images/recipe-pasta.svg',
    tags: ['Schnell', 'Liebling'],
    ingredients: ['200 g Pasta', '1 Bio-Zitrone', '60 g Parmesan', '100 ml Sahne', '1 Knoblauchzehe'],
    steps: ['Pasta in Salzwasser bissfest kochen und etwas Kochwasser auffangen.', 'Zitronenabrieb, Sahne und Knoblauch sanft erwärmen.', 'Pasta mit Sauce, Parmesan und einem Schuss Kochwasser vermengen.'],
  },
  {
    id: 'roast-vegetables',
    name: 'Ofengemüse mit Kräuterdip',
    description: 'Knuspriges Saisongemüse direkt vom Blech, dazu ein frischer Joghurt-Kräuter-Dip.',
    category: 'Vegetarisch',
    minutes: 40,
    servings: 3,
    rating: 'Einfach',
    image: '/images/recipe-vegetables.svg',
    tags: ['Blechgericht', 'Saisonal'],
    ingredients: ['2 Karotten', '1 Süßkartoffel', '1 rote Zwiebel', '2 EL Olivenöl', '150 g Naturjoghurt'],
    steps: ['Gemüse in mundgerechte Stücke schneiden und mit Öl würzen.', 'Bei 210 °C etwa 30 Minuten rösten.', 'Joghurt mit gehackten Kräutern verrühren und dazu servieren.'],
  },
  {
    id: 'salmon-bowl',
    name: 'Lachs-Bowl mit Kräuterreis',
    description: 'Zarter Lachs auf duftendem Reis mit Gurke und einer zitronigen Kräutersauce.',
    category: 'Fisch',
    minutes: 30,
    servings: 2,
    rating: 'Einfach',
    image: '/images/recipe-bowl.svg',
    tags: ['Proteinreich', 'Frisch'],
    ingredients: ['2 Lachsfilets', '150 g Basmatireis', '½ Gurke', '1 Limette', '1 Bund Dill'],
    steps: ['Reis nach Packungsangabe garen und mit Dill verfeinern.', 'Lachs in einer Pfanne braten, bis er innen gerade eben gar ist.', 'Mit Gurke und Limettensauce in Schalen anrichten.'],
  },
  {
    id: 'tomato-soup',
    name: 'Samtige Tomatensuppe',
    description: 'Reife Tomaten, Basilikum und geröstetes Brot für ein kleines Stück Spätsommer.',
    category: 'Vegan',
    minutes: 35,
    servings: 4,
    rating: 'Einfach',
    image: '/images/recipe-soup.svg',
    tags: ['Vegan', 'Gut vorzubereiten'],
    ingredients: ['800 g Tomaten', '1 rote Zwiebel', '500 ml Gemüsebrühe', '1 Bund Basilikum', '4 Scheiben Brot'],
    steps: ['Zwiebel glasig dünsten, Tomaten zugeben und kurz anrösten.', 'Mit Gemüsebrühe aufgießen und 20 Minuten köcheln lassen.', 'Pürieren, abschmecken und mit Basilikum servieren.'],
  },
];

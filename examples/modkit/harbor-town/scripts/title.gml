/// title(donated): current patron title.
var donated = argument0;
if (donated >= 1000000000000000) return "Harbor Legend";
if (donated >= 1000000000000) return "Harbor Tycoon";
if (donated >= 1000000000) return "Harbor Baron";
if (donated >= 1000000) return "Harbor Patron";
return "";

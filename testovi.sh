#!/bin/bash

echo "Ovo nije predvidjeno za kompletno pokretanje!"
read
read
echo "Ako pritisnes jos jednom, pokrenuce se nepoznat broj nedefinisanih komandi"
read

####################################################################################

# Test 1: normalna simulacija
curl -X POST http://localhost:3000/simulate/start \
  -H "Content-Type: application/json" \
  -d '{"deviceCount": 1, "intervalMs": 1000, "durationMs": 15000}'
####################################################################################

echo
echo "Sledeca"
read

# Test 2: forceAlert - proveri da ALERT radi
curl -X POST http://localhost:3000/simulate/start \
  -H "Content-Type: application/json" \
  -d '{"deviceCount": 1, "intervalMs": 1000, "durationMs": 15000, "forceAlert": true}'


exit 0
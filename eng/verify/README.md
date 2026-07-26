# Screen verification scripts

These drive the running app over real HTTP — cookies, antiforgery tokens, form posts — so they
exercise exactly what an operator's browser does. They are the repeatable form of spec 0012's
acceptance criteria; the §9A.4 *timed* tests are separate and belong to spec 0010.

```sh
# start the app first (defaults to http://localhost:5199)
cd src/Hms.Web && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 dotnet run

./nav-smoke.sh jashim 'Demo#1234' / /registration /registration/new /appointments
python3 golden-thread.py        # register → serial → order → pay → lab → verify → deliver → day-close → dashboard
python3 discount-and-dues.py    # discount above threshold → approval → invoice → due collection
```

Both Python scripts assume a **freshly seeded database** (they assert exact money totals):

```sh
docker exec hms-dev-db psql -U postgres -d postgres \
  -c "DROP DATABASE IF EXISTS hms WITH (FORCE);" -c "CREATE DATABASE hms;"
```

`golden-thread.py` must run before `discount-and-dues.py` — the latter bills the patient the
former registers.

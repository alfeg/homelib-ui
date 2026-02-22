window.myHomeLibTheme = {
    get: function () {
        return window.localStorage.getItem("myhomelib-theme");
    },
    set: function (value) {
        window.localStorage.setItem("myhomelib-theme", value);
    }
};

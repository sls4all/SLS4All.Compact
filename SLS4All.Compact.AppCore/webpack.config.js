const path = require("path");
const MiniCssExtractPlugin = require('mini-css-extract-plugin');
const CopyPlugin = require("copy-webpack-plugin");

module.exports = {
    mode: 'development',
    entry: ['./Scripts/app.js'],
    devtool: "nosources-source-map",
    //devtool: "eval",
    module: {
        rules: [
            {
                test: /\.js$/,
                enforce: "pre",
                use: ["source-map-loader"],
            },
            {
                test: /\.woff2?$|\.ttf$|\.eot$|\.svg|\.otf|\.svg$/,
                use: [{
                    loader: "file-loader"
                }]
            },
            {
                test: /\.css$/i,
                use: [
                    {
                        loader: MiniCssExtractPlugin.loader,
                    },
                    "css-loader"
                ]
            },
        ],
    },
    plugins: [
        new MiniCssExtractPlugin({
            filename: "bundle.css",
        }),
        new CopyPlugin({
            patterns: [
                { from: "node_modules/bootstrap-icons", to: "../vendors/bootstrap-icons" },
                { from: "node_modules/panzoom/dist/panzoom.min.js", to: "../vendors/panzoom/panzoom.min.js" },
            ],
        })
    ],
    output: {
        filename: 'bundle.js',
        path: path.resolve(__dirname, 'wwwroot/bundles'),
         devtoolModuleFilenameTemplate: function (info) {
            return "file:///" + encodeURI(info.absoluteResourcePath);
        }
   },
}
